from __future__ import annotations

import base64
import ctypes
import hashlib
import json
import os
import platform
import re
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import asdict, dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any

from PySide6.QtCore import QObject, QPoint, QRect, Qt, QTimer, QUrl, Signal
from PySide6.QtGui import QAction, QColor, QCursor, QDesktopServices, QGuiApplication, QIcon, QImage, QPainter, QPixmap
from PySide6.QtWidgets import (
    QApplication,
    QCheckBox,
    QDialog,
    QFileDialog,
    QFormLayout,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QListWidget,
    QListWidgetItem,
    QMainWindow,
    QMenu,
    QMessageBox,
    QPushButton,
    QPlainTextEdit,
    QSpinBox,
    QDoubleSpinBox,
    QSystemTrayIcon,
    QTabWidget,
    QTextBrowser,
    QVBoxLayout,
    QWidget,
)

try:
    from pynput import keyboard
except Exception:  # pragma: no cover - handled at runtime
    keyboard = None


def resolve_root_dir() -> Path:
    env_root = os.environ.get("ASKSHOT_ROOT_DIR")
    if env_root:
        return Path(env_root).expanduser().resolve()

    if getattr(sys, "frozen", False):
        executable_dir = Path(sys.executable).resolve().parent
        for candidate in (executable_dir, executable_dir.parent, Path.cwd()):
            if (candidate / "services" / "main.py").exists():
                return candidate
        return executable_dir

    return Path(__file__).resolve().parents[2]


ROOT_DIR = resolve_root_dir()


def bundled_service_executable() -> Path | None:
    if not getattr(sys, "frozen", False):
        return None

    executable = Path(sys.executable).resolve()
    candidates = [
        executable.parent / "askshot-service",
        executable.parent / "askshot-service.exe",
        executable.parent.parent / "Resources" / "askshot-service",
        executable.parent.parent / "Resources" / "askshot-service.exe",
    ]
    for candidate in candidates:
        if candidate.exists():
            return candidate
    return None


def resolve_services_dir() -> Path:
    env_services = os.environ.get("ASKSHOT_SERVICES_DIR")
    if env_services:
        return Path(env_services).expanduser().resolve()

    service_exe = bundled_service_executable()
    if service_exe is not None:
        return service_exe.parent

    return (ROOT_DIR / "services").resolve()


SERVICES_DIR = resolve_services_dir()
APP_SUPPORT_DIR = Path.home() / "Library" / "Application Support" / "AskShot"
DATA_DIR = APP_SUPPORT_DIR / "data"
CONFIG_PATH = APP_SUPPORT_DIR / "appsettings.mac.json"
LOG_DIR = APP_SUPPORT_DIR / "logs"
BASE_URL = "http://127.0.0.1:8900"
IS_MACOS = platform.system() == "Darwin"


class CaptureCancelled(Exception):
    pass


@dataclass
class LlmConfig:
    endpoint: str = ""
    api_key: str = ""
    model: str = "qwen2.5-vl-3b-instruct"
    temperature: float = 0.7
    max_tokens: int = 2048


@dataclass
class DataConfig:
    save_screenshots: bool = False
    screenshot_path: str = ""
    history_retention_days: int = 30


@dataclass
class HotkeyConfig:
    capture_and_analyze: str = "Ctrl+Shift+A"


@dataclass
class AppConfig:
    llm: LlmConfig = field(default_factory=LlmConfig)
    data: DataConfig = field(default_factory=DataConfig)
    hotkeys: HotkeyConfig = field(default_factory=HotkeyConfig)

    @staticmethod
    def load() -> "AppConfig":
        if not CONFIG_PATH.exists():
            return AppConfig()

        try:
            raw = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
            return AppConfig(
                llm=LlmConfig(**raw.get("llm", {})),
                data=DataConfig(**raw.get("data", {})),
                hotkeys=HotkeyConfig(**raw.get("hotkeys", {})),
            )
        except (OSError, TypeError, json.JSONDecodeError):
            backup = CONFIG_PATH.with_suffix(".invalid.json")
            try:
                CONFIG_PATH.replace(backup)
            except OSError:
                pass
            return AppConfig()

    def save(self) -> None:
        APP_SUPPORT_DIR.mkdir(parents=True, exist_ok=True)
        CONFIG_PATH.write_text(json.dumps(asdict(self), ensure_ascii=False, indent=2), encoding="utf-8")

    def api_payload(self) -> dict[str, Any]:
        return {
            "endpoint": self.llm.endpoint.strip(),
            "api_key": self.llm.api_key.strip(),
            "model": self.llm.model.strip(),
            "temperature": self.llm.temperature,
            "max_tokens": self.llm.max_tokens,
        }

    def screenshots_dir(self) -> Path:
        if self.data.screenshot_path.strip():
            return Path(self.data.screenshot_path).expanduser()
        return DATA_DIR / "screenshots"


class ApiClient:
    def request_json(self, method: str, path: str, payload: dict[str, Any] | None = None, timeout: float = 60) -> Any:
        body = None if payload is None else json.dumps(payload).encode("utf-8")
        req = urllib.request.Request(
            f"{BASE_URL}{path}",
            data=body,
            method=method,
            headers={"Content-Type": "application/json"},
        )
        try:
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                data = resp.read().decode("utf-8")
                return json.loads(data) if data else None
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")
            raise RuntimeError(f"HTTP {exc.code}: {detail}") from exc

    def is_healthy(self) -> bool:
        try:
            self.request_json("GET", "/health", timeout=2)
            return True
        except Exception:
            return False

    def analyze(self, image_base64: str, config: AppConfig, question: str | None = None) -> str:
        result = self.request_json(
            "POST",
            "/analyze",
            {
                "image_base64": image_base64,
                "user_question": question,
                "api_config": config.api_payload(),
            },
            timeout=90,
        )
        return result.get("summary", "")

    def test_config(self, config: AppConfig) -> dict[str, Any]:
        return self.request_json("POST", "/config/test", {"api_config": config.api_payload()}, timeout=15)

    def save_history(
        self,
        analysis: str,
        user_question: str = "",
        screenshot_path: str | None = None,
        image_hash: str = "",
    ) -> None:
        self.request_json(
            "POST",
            "/history/save",
            {
                "analysis": analysis,
                "user_question": user_question,
                "screenshot_path": screenshot_path,
                "image_hash": image_hash,
            },
            timeout=10,
        )

    def recent_history(self, limit: int = 20) -> list[dict[str, Any]]:
        return self.request_json("GET", f"/history/recent?limit={limit}&hours=87600", timeout=10).get("results", [])

    def search_history(self, query: str, limit: int = 20) -> list[dict[str, Any]]:
        return self.request_json("POST", "/history/search", {"query": query, "limit": limit}, timeout=10).get("results", [])

    def toggle_favorite(self, record_id: str) -> bool:
        quoted = urllib.parse.quote(record_id, safe="")
        return bool(self.request_json("POST", f"/history/favorite/{quoted}", timeout=10).get("is_favorite"))


class PythonServiceManager:
    def __init__(self, api: ApiClient):
        self.api = api
        self.process: subprocess.Popen[str] | None = None

    def start(self) -> None:
        if self.api.is_healthy():
            return

        service_exe = bundled_service_executable()
        if service_exe is None and not (SERVICES_DIR / "main.py").exists():
            raise FileNotFoundError(f"Cannot find Python service at {SERVICES_DIR}")

        LOG_DIR.mkdir(parents=True, exist_ok=True)
        DATA_DIR.mkdir(parents=True, exist_ok=True)
        stdout = (LOG_DIR / "python-service.log").open("a", encoding="utf-8")
        stderr = (LOG_DIR / "python-service.err.log").open("a", encoding="utf-8")
        env = os.environ.copy()
        env["ASKSHOT_DATA_DIR"] = str(DATA_DIR)

        command = [str(service_exe)] if service_exe else [sys.executable, "main.py"]
        cwd = service_exe.parent if service_exe else SERVICES_DIR

        self.process = subprocess.Popen(
            command,
            cwd=cwd,
            env=env,
            stdout=stdout,
            stderr=stderr,
            text=True,
        )

        deadline = time.time() + 30
        while time.time() < deadline:
            if self.api.is_healthy():
                return
            if self.process.poll() is not None:
                raise RuntimeError(f"Python service exited with code {self.process.returncode}")
            time.sleep(0.3)
        raise TimeoutError("Python service did not become healthy within 30 seconds")

    def stop(self) -> None:
        if self.process and self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.process.kill()
        self.process = None

    def restart(self) -> None:
        self.stop()
        self.start()


class HotkeyListener(QObject):
    activated = Signal()
    error = Signal(str)

    def __init__(self, hotkey: str):
        super().__init__()
        self.hotkey = hotkey
        self._pressed: set[str] = set()
        self._armed = True
        self._listener: Any = None

    def start(self) -> None:
        if keyboard is None:
            self.error.emit("pynput 未安装，无法注册全局热键。")
            return
        try:
            self._listener = keyboard.Listener(on_press=self._on_press, on_release=self._on_release)
            self._listener.daemon = True
            self._listener.start()
        except Exception as exc:
            self.error.emit(f"无法注册全局热键。请检查辅助功能权限。\n{exc}")

    def stop(self) -> None:
        if self._listener:
            self._listener.stop()
            self._listener = None

    def set_hotkey(self, hotkey: str) -> None:
        self.hotkey = hotkey
        self._pressed.clear()
        self._armed = True

    def _on_press(self, key: Any) -> None:
        name = self._key_name(key)
        if name:
            self._pressed.add(name)
        if self._armed and self._matches():
            self._armed = False
            self.activated.emit()

    def _on_release(self, key: Any) -> None:
        name = self._key_name(key)
        if name:
            self._pressed.discard(name)
        if not self._matches():
            self._armed = True

    def _matches(self) -> bool:
        required = parse_hotkey(self.hotkey)
        return required.issubset(self._pressed)

    @staticmethod
    def _key_name(key: Any) -> str | None:
        if hasattr(key, "char") and key.char:
            return key.char.lower()
        text = str(key).replace("Key.", "").lower()
        aliases = {
            "ctrl_l": "ctrl",
            "ctrl_r": "ctrl",
            "cmd": "cmd",
            "cmd_l": "cmd",
            "cmd_r": "cmd",
            "shift_l": "shift",
            "shift_r": "shift",
            "alt_l": "alt",
            "alt_r": "alt",
        }
        return aliases.get(text, text)


def parse_hotkey(hotkey: str) -> set[str]:
    aliases = {"control": "ctrl", "command": "cmd", "option": "alt"}
    parts = {aliases.get(part.strip().lower(), part.strip().lower()) for part in hotkey.split("+")}
    return {part for part in parts if part}


def open_macos_privacy_settings(anchor: str) -> None:
    if not IS_MACOS:
        return
    QDesktopServices.openUrl(QUrl(f"x-apple.systempreferences:com.apple.preference.security?{anchor}"))


def has_screen_capture_access() -> bool:
    if not IS_MACOS:
        return True
    try:
        core_graphics = ctypes.CDLL(
            "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics"
        )
        core_graphics.CGPreflightScreenCaptureAccess.restype = ctypes.c_bool
        return bool(core_graphics.CGPreflightScreenCaptureAccess())
    except Exception:
        return False


def request_screen_capture_access() -> bool:
    if not IS_MACOS:
        return True
    try:
        core_graphics = ctypes.CDLL(
            "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics"
        )
        core_graphics.CGRequestScreenCaptureAccess.restype = ctypes.c_bool
        return bool(core_graphics.CGRequestScreenCaptureAccess())
    except Exception:
        return False


class DesktopCapture:
    def __init__(self):
        self.origin = QPoint(0, 0)
        self.image = QImage()

    def capture(self) -> tuple[QImage, QPoint]:
        screens = QGuiApplication.screens()
        if not screens:
            raise RuntimeError("没有可用屏幕。")

        union = screens[0].geometry()
        for screen in screens[1:]:
            union = union.united(screen.geometry())

        image = QImage(union.size(), QImage.Format.Format_ARGB32)
        image.fill(QColor("black"))
        painter = QPainter(image)
        captured_any = False
        for screen in screens:
            pixmap = screen.grabWindow(0)
            if pixmap.isNull():
                continue
            captured_any = True
            target = screen.geometry().translated(-union.topLeft())
            painter.drawPixmap(target, pixmap)
        painter.end()

        if not captured_any or self._looks_like_permission_blackout(image):
            raise RuntimeError(
                "无法读取屏幕内容。请在 macOS System Settings -> Privacy & Security -> "
                "Screen Recording 中允许当前终端或 AskShot，然后重启 AskShot。"
            )

        self.origin = union.topLeft()
        self.image = image
        return image, self.origin

    @staticmethod
    def _looks_like_permission_blackout(image: QImage) -> bool:
        if image.isNull() or image.width() <= 0 or image.height() <= 0:
            return True

        samples = 0
        bright_samples = 0
        colors: set[int] = set()
        step_x = max(1, image.width() // 12)
        step_y = max(1, image.height() // 12)
        for y in range(step_y // 2, image.height(), step_y):
            for x in range(step_x // 2, image.width(), step_x):
                color = QColor(image.pixel(x, y))
                brightness = color.red() + color.green() + color.blue()
                if brightness > 30:
                    bright_samples += 1
                colors.add(image.pixel(x, y) & 0x00FFFFFF)
                samples += 1

        return samples > 0 and bright_samples == 0 and len(colors) <= 2

    def crop_to_base64(self, global_rect: QRect) -> tuple[str, QImage]:
        local = global_rect.translated(-self.origin)
        local = local.intersected(QRect(QPoint(0, 0), self.image.size()))
        if local.width() < 2 or local.height() < 2:
            raise RuntimeError("截图区域太小。")
        cropped = self.image.copy(local)
        return image_to_base64(cropped), cropped

    def capture_interactive_region(self) -> tuple[str, QImage]:
        handle = tempfile.NamedTemporaryFile(prefix="askshot-", suffix=".png", delete=False)
        path = Path(handle.name)
        handle.close()
        path.unlink(missing_ok=True)

        try:
            result = subprocess.run(
                ["screencapture", "-i", "-x", "-t", "png", str(path)],
                check=False,
            )
            if result.returncode != 0 or not path.exists() or path.stat().st_size == 0:
                raise CaptureCancelled()

            image = QImage(str(path))
            if image.isNull() or image.width() < 2 or image.height() < 2:
                raise RuntimeError("截图失败：未生成有效图片。")
            return image_to_base64(image), image
        finally:
            path.unlink(missing_ok=True)


def image_to_base64(image: QImage) -> str:
    from PySide6.QtCore import QBuffer, QByteArray, QIODevice

    data = QByteArray()
    buffer = QBuffer(data)
    buffer.open(QIODevice.OpenModeFlag.WriteOnly)
    image.save(buffer, "PNG")
    return base64.b64encode(bytes(data)).decode("ascii")


def clean_display_text(text: str) -> str:
    if not text:
        return ""
    cleaned = text.replace("\r\n", "\n").replace("\r", "\n")
    cleaned = re.sub(r"```[a-zA-Z0-9_-]*\n?", "", cleaned)
    cleaned = cleaned.replace("```", "")
    cleaned = re.sub(r"(?m)^\s{0,3}#{1,6}\s*", "", cleaned)
    cleaned = re.sub(r"\*\*([^*\n]+)\*\*", r"\1", cleaned)
    cleaned = re.sub(r"__([^_\n]+)__", r"\1", cleaned)
    cleaned = re.sub(r"(?<!\*)\*([^*\n]+)\*(?!\*)", r"\1", cleaned)
    cleaned = re.sub(r"(?m)^\s*[*+-]\s+", "• ", cleaned)
    cleaned = re.sub(r"(?m)^\s*[-*_]{3,}\s*$", "", cleaned)
    cleaned = re.sub(r"[ \t]+\n", "\n", cleaned)
    cleaned = re.sub(r"\n{3,}", "\n\n", cleaned)
    return cleaned.strip()


class SelectionOverlay(QWidget):
    selected = Signal(object)

    def __init__(self, screenshot: QImage, origin: QPoint):
        super().__init__()
        self.screenshot = screenshot
        self.origin = origin
        self.start: QPoint | None = None
        self.current: QPoint | None = None
        self.setWindowFlags(
            Qt.WindowType.FramelessWindowHint
            | Qt.WindowType.WindowStaysOnTopHint
            | Qt.WindowType.Tool
        )
        self.setCursor(Qt.CursorShape.CrossCursor)
        self.setGeometry(QRect(origin, screenshot.size()))
        self.setMouseTracking(True)

    def paintEvent(self, event: Any) -> None:
        painter = QPainter(self)
        painter.drawImage(self.rect(), self.screenshot)
        painter.fillRect(self.rect(), QColor(0, 0, 0, 90))
        if self.start and self.current:
            rect = QRect(self.start, self.current).normalized()
            painter.drawImage(rect, self.screenshot.copy(rect))
            painter.fillRect(rect, QColor(0, 102, 255, 35))
            pen = painter.pen()
            pen.setColor(QColor(0, 102, 255))
            pen.setWidth(2)
            painter.setPen(pen)
            painter.drawRect(rect)
            painter.setPen(QColor("white"))
            painter.drawText(rect.bottomRight() + QPoint(8, 18), f"{rect.width()} x {rect.height()}")
        painter.end()

    def mousePressEvent(self, event: Any) -> None:
        if event.button() == Qt.MouseButton.LeftButton:
            self.start = event.position().toPoint()
            self.current = self.start
            self.update()

    def mouseMoveEvent(self, event: Any) -> None:
        if self.start:
            self.current = event.position().toPoint()
            self.update()

    def mouseReleaseEvent(self, event: Any) -> None:
        if event.button() != Qt.MouseButton.LeftButton or not self.start:
            return
        self.current = event.position().toPoint()
        local = QRect(self.start, self.current).normalized()
        self.close()
        if local.width() < 10 or local.height() < 10:
            self.selected.emit(None)
            return
        self.selected.emit(local.translated(self.origin))

    def keyPressEvent(self, event: Any) -> None:
        if event.key() == Qt.Key.Key_Escape:
            self.close()
            self.selected.emit(None)


class ResultPopup(QWidget):
    follow_up = Signal(str)

    def __init__(self):
        super().__init__()
        self.pinned = False
        self.setWindowTitle("AskShot")
        self.setWindowFlags(Qt.WindowType.Tool)
        self.resize(430, 320)
        self.pin_timer = QTimer(self)
        self.pin_timer.setInterval(1000)
        self.pin_timer.timeout.connect(self._keep_pinned_on_top)
        self.setStyleSheet(
            """
            QWidget {
                background: #fafafa;
                color: #202124;
                font-size: 14px;
            }
            QTextBrowser {
                border: none;
                background: #fafafa;
                line-height: 1.45;
            }
            QLineEdit {
                border: none;
                border-radius: 6px;
                padding: 7px 8px;
                background: #f1f3f4;
            }
            QPushButton {
                border: 1px solid #dadce0;
                border-radius: 6px;
                padding: 5px 10px;
                background: #ffffff;
            }
            QPushButton:hover {
                background: #f1f3f4;
            }
            """
        )

        layout = QVBoxLayout(self)
        layout.setContentsMargins(14, 14, 14, 14)
        layout.setSpacing(10)
        header = QHBoxLayout()
        title = QLabel("解释")
        title.setStyleSheet("font-weight: 600;")
        header.addWidget(title)
        header.addStretch()
        copy_btn = QPushButton("复制")
        copy_btn.clicked.connect(self._copy)
        self.pin_btn = QPushButton("固定")
        self.pin_btn.clicked.connect(self._toggle_pin)
        close_btn = QPushButton("关闭")
        close_btn.clicked.connect(self.close)
        header.addWidget(copy_btn)
        header.addWidget(self.pin_btn)
        header.addWidget(close_btn)

        self.text = QTextBrowser()
        self.question = QLineEdit()
        self.question.setPlaceholderText("继续追问...")
        self.question.returnPressed.connect(self._ask)
        ask_btn = QPushButton("Ask")
        ask_btn.clicked.connect(self._ask)

        input_row = QHBoxLayout()
        input_row.addWidget(self.question)
        input_row.addWidget(ask_btn)

        layout.addLayout(header)
        layout.addWidget(self.text)
        layout.addLayout(input_row)

    def show_text(self, text: str) -> None:
        self.text.setPlainText(clean_display_text(text))
        self._move_to_bottom_right()
        self.show()
        self.raise_()
        self.activateWindow()

    def append_text(self, text: str) -> None:
        self.text.append("")
        self.text.append(clean_display_text(text))

    def _ask(self) -> None:
        question = self.question.text().strip()
        if not question:
            return
        self.question.clear()
        self.text.append("")
        self.text.append(f"追问: {question}")
        self.text.append("分析中...")
        self.follow_up.emit(question)

    def _toggle_pin(self) -> None:
        self.pinned = not self.pinned
        flags = Qt.WindowType.Tool
        if self.pinned:
            flags |= Qt.WindowType.WindowStaysOnTopHint
        self.setWindowFlags(flags)
        self.pin_btn.setText("已固定" if self.pinned else "固定")
        self.show()
        self.raise_()
        self.activateWindow()
        if self.pinned:
            self.pin_timer.start()
        else:
            self.pin_timer.stop()

    def _copy(self) -> None:
        QApplication.clipboard().setText(self.text.toPlainText())

    def _keep_pinned_on_top(self) -> None:
        if not self.pinned or not self.isVisible():
            return
        self.raise_()

    def keyPressEvent(self, event: Any) -> None:
        if event.key() == Qt.Key.Key_Escape and not self.pinned:
            self.close()

    def _move_to_bottom_right(self) -> None:
        screen = QGuiApplication.screenAt(QCursor.pos()) or QGuiApplication.primaryScreen()
        area = screen.availableGeometry()
        self.move(area.right() - self.width() - 16, area.bottom() - self.height() - 16)


class ConfigWindow(QMainWindow):
    restart_requested = Signal()
    settings_saved = Signal()

    def __init__(self, config: AppConfig, api: ApiClient):
        super().__init__()
        self.config = config
        self.api = api
        self.setWindowTitle("AskShot Console")
        self.resize(620, 460)

        tabs = QTabWidget()
        tabs.addTab(self._general_tab(), "General")
        tabs.addTab(self._llm_tab(), "LLM")
        tabs.addTab(self._data_tab(), "Data")
        tabs.addTab(self._service_tab(), "Service")
        self.setCentralWidget(tabs)

    def _general_tab(self) -> QWidget:
        page = QWidget()
        form = QFormLayout(page)
        self.hotkey_input = QLineEdit(self.config.hotkeys.capture_and_analyze)
        self.hotkey_input.setPlaceholderText("Ctrl+Shift+A or Cmd+Shift+A")
        save_btn = QPushButton("Save")
        save_btn.clicked.connect(self._save)

        permission_row = QHBoxLayout()
        accessibility = QPushButton("Open Accessibility")
        accessibility.clicked.connect(lambda: open_macos_privacy_settings("Privacy_Accessibility"))
        screen_recording = QPushButton("Open Screen Recording")
        screen_recording.clicked.connect(lambda: open_macos_privacy_settings("Privacy_ScreenCapture"))
        permission_row.addWidget(accessibility)
        permission_row.addWidget(screen_recording)
        permission_row.addStretch()

        form.addRow("Capture Hotkey", self.hotkey_input)
        form.addRow(permission_row)
        form.addRow(save_btn)
        return page

    def _llm_tab(self) -> QWidget:
        page = QWidget()
        form = QFormLayout(page)
        self.endpoint = QLineEdit(self.config.llm.endpoint)
        self.api_key = QLineEdit(self.config.llm.api_key)
        self.api_key.setEchoMode(QLineEdit.EchoMode.Password)
        self.model = QLineEdit(self.config.llm.model)
        self.temperature = QDoubleSpinBox()
        self.temperature.setRange(0, 2)
        self.temperature.setSingleStep(0.1)
        self.temperature.setValue(self.config.llm.temperature)
        self.max_tokens = QSpinBox()
        self.max_tokens.setRange(1, 100000)
        self.max_tokens.setValue(self.config.llm.max_tokens)
        self.connection_status = QLabel("")

        test_btn = QPushButton("Test Connection")
        test_btn.clicked.connect(self._test_connection)
        save_btn = QPushButton("Save")
        save_btn.clicked.connect(self._save)

        row = QHBoxLayout()
        row.addWidget(test_btn)
        row.addWidget(save_btn)
        row.addStretch()

        form.addRow("Endpoint", self.endpoint)
        form.addRow("API Key", self.api_key)
        form.addRow("Model", self.model)
        form.addRow("Temperature", self.temperature)
        form.addRow("Max Tokens", self.max_tokens)
        form.addRow(row)
        form.addRow(self.connection_status)
        return page

    def _data_tab(self) -> QWidget:
        page = QWidget()
        form = QFormLayout(page)
        self.save_screens = QCheckBox("Save original screenshots")
        self.save_screens.setChecked(self.config.data.save_screenshots)
        self.screenshot_path = QLineEdit(str(self.config.screenshots_dir()))
        browse = QPushButton("Browse")
        browse.clicked.connect(self._browse_screenshot_path)
        path_row = QHBoxLayout()
        path_row.addWidget(self.screenshot_path)
        path_row.addWidget(browse)
        self.retention = QSpinBox()
        self.retention.setRange(1, 3650)
        self.retention.setValue(self.config.data.history_retention_days)
        clear_btn = QPushButton("Clear History")
        clear_btn.clicked.connect(self._clear_history)
        open_btn = QPushButton("Open Data Folder")
        open_btn.clicked.connect(lambda: QDesktopServices.openUrl(QUrl.fromLocalFile(str(DATA_DIR))))
        save_btn = QPushButton("Save")
        save_btn.clicked.connect(self._save)
        buttons = QHBoxLayout()
        buttons.addWidget(clear_btn)
        buttons.addWidget(open_btn)
        buttons.addWidget(save_btn)
        buttons.addStretch()
        form.addRow(self.save_screens)
        form.addRow("Screenshot Path", path_row)
        form.addRow("History Retention Days", self.retention)
        form.addRow(buttons)
        return page

    def _service_tab(self) -> QWidget:
        page = QWidget()
        layout = QVBoxLayout(page)
        self.service_status = QLabel("Python Service: checking...")
        restart = QPushButton("Restart Service")
        restart.clicked.connect(self.restart_requested.emit)
        refresh = QPushButton("Refresh Status")
        refresh.clicked.connect(self.refresh_service_status)
        layout.addWidget(self.service_status)
        layout.addWidget(restart)
        layout.addWidget(refresh)
        layout.addStretch()
        QTimer.singleShot(0, self.refresh_service_status)
        return page

    def _save(self) -> None:
        self.config.llm.endpoint = self.endpoint.text().strip()
        self.config.llm.api_key = self.api_key.text().strip()
        self.config.llm.model = self.model.text().strip()
        self.config.llm.temperature = float(self.temperature.value())
        self.config.llm.max_tokens = int(self.max_tokens.value())
        self.config.data.save_screenshots = self.save_screens.isChecked()
        self.config.data.screenshot_path = self.screenshot_path.text().strip()
        self.config.data.history_retention_days = int(self.retention.value())
        self.config.hotkeys.capture_and_analyze = self.hotkey_input.text().strip() or "Ctrl+Shift+A"
        self.config.save()
        self.settings_saved.emit()

    def _test_connection(self) -> None:
        self._save()
        try:
            if not self.api.is_healthy():
                self.connection_status.setText("Python service is not responding.")
                return
            result = self.api.test_config(self.config)
            self.connection_status.setText(result.get("message", "No message"))
        except Exception as exc:
            self.connection_status.setText(str(exc))

    def _browse_screenshot_path(self) -> None:
        path = QFileDialog.getExistingDirectory(self, "Choose Screenshot Folder", str(self.config.screenshots_dir()))
        if path:
            self.screenshot_path.setText(path)
            self._save()

    def _clear_history(self) -> None:
        if QMessageBox.question(self, "Confirm", "Clear all history records?") != QMessageBox.StandardButton.Yes:
            return
        history_dir = DATA_DIR / "history"
        for file in history_dir.glob("*.json"):
            file.unlink(missing_ok=True)
        QMessageBox.information(self, "Done", "History cleared.")

    def refresh_service_status(self) -> None:
        self.service_status.setText("Python Service: running" if self.api.is_healthy() else "Python Service: stopped")


class HistoryWindow(QDialog):
    def __init__(self, api: ApiClient):
        super().__init__()
        self.api = api
        self.records: list[dict[str, Any]] = []
        self.setWindowTitle("AskShot History")
        self.resize(760, 520)

        layout = QVBoxLayout(self)
        search_row = QHBoxLayout()
        self.query = QLineEdit()
        self.query.setPlaceholderText("Search history...")
        self.query.returnPressed.connect(self.search)
        search_btn = QPushButton("Search")
        search_btn.clicked.connect(self.search)
        recent_btn = QPushButton("Recent")
        recent_btn.clicked.connect(self.load_recent)
        self.favorite_btn = QPushButton("Favorite")
        self.favorite_btn.clicked.connect(self.toggle_favorite)
        search_row.addWidget(self.query)
        search_row.addWidget(search_btn)
        search_row.addWidget(recent_btn)
        search_row.addWidget(self.favorite_btn)

        body = QHBoxLayout()
        self.list_widget = QListWidget()
        self.list_widget.currentRowChanged.connect(self._show_record)
        self.details = QPlainTextEdit()
        self.details.setReadOnly(True)
        body.addWidget(self.list_widget, 1)
        body.addWidget(self.details, 2)

        layout.addLayout(search_row)
        layout.addLayout(body)
        QTimer.singleShot(0, self.load_recent)

    def load_recent(self) -> None:
        try:
            self._set_records(self.api.recent_history())
        except Exception as exc:
            QMessageBox.warning(self, "History", str(exc))

    def search(self) -> None:
        query = self.query.text().strip()
        try:
            self._set_records(self.api.search_history(query) if query else self.api.recent_history())
        except Exception as exc:
            QMessageBox.warning(self, "History", str(exc))

    def toggle_favorite(self) -> None:
        row = self.list_widget.currentRow()
        if row < 0 or row >= len(self.records):
            return
        record = self.records[row]
        try:
            record["is_favorite"] = self.api.toggle_favorite(record["id"])
            self._show_record(row)
        except Exception as exc:
            QMessageBox.warning(self, "Favorite", str(exc))

    def _set_records(self, records: list[dict[str, Any]]) -> None:
        self.records = records
        self.list_widget.clear()
        for record in records:
            marker = "*" if record.get("is_favorite") else " "
            text = (record.get("analysis") or "").splitlines()[0][:80]
            self.list_widget.addItem(QListWidgetItem(f"{marker} {record.get('id', '')}  {text}"))
        if records:
            self.list_widget.setCurrentRow(0)
        else:
            self.details.setPlainText("")

    def _show_record(self, row: int) -> None:
        if row < 0 or row >= len(self.records):
            return
        record = self.records[row]
        self.favorite_btn.setText("Unfavorite" if record.get("is_favorite") else "Favorite")
        self.details.setPlainText(json.dumps(record, ensure_ascii=False, indent=2))


class Controller(QObject):
    analysis_done = Signal(str, str, bool)
    analysis_failed = Signal(str)

    def __init__(self):
        super().__init__()
        self.config = AppConfig.load()
        self.api = ApiClient()
        self.service = PythonServiceManager(self.api)
        self.capture = DesktopCapture()
        self.popup: ResultPopup | None = None
        self.pinned_popups: list[ResultPopup] = []
        self.config_window: ConfigWindow | None = None
        self.history_window: HistoryWindow | None = None
        self.overlay: SelectionOverlay | None = None
        self.last_image_base64 = ""
        self.last_image: QImage | None = None
        self.last_hash = ""
        self.analysis_done.connect(self._show_analysis)
        self.analysis_failed.connect(self._show_error)

        self.tray = QSystemTrayIcon(self._icon())
        self.tray.setToolTip("AskShot")
        self.tray.activated.connect(self._tray_activated)
        self.tray.setContextMenu(self._menu())
        self.tray.show()

        self.hotkey = HotkeyListener(self.config.hotkeys.capture_and_analyze)
        self.hotkey.activated.connect(self.capture_and_analyze)
        self.hotkey.error.connect(lambda msg: QMessageBox.warning(None, "Hotkey", msg))
        self.hotkey.start()

        try:
            self.service.start()
        except Exception as exc:
            QMessageBox.warning(None, "AskShot", f"Python service failed to start:\n{exc}")

    def _menu(self) -> QMenu:
        menu = QMenu()
        capture_action = QAction("Capture and Analyze", menu)
        capture_action.triggered.connect(self.capture_and_analyze)
        console_action = QAction("Console", menu)
        console_action.triggered.connect(self.open_console)
        history_action = QAction("Search History", menu)
        history_action.triggered.connect(self.open_history)
        restart_action = QAction("Restart Service", menu)
        restart_action.triggered.connect(self.restart_service)
        accessibility_action = QAction("Open Accessibility Settings", menu)
        accessibility_action.triggered.connect(lambda: open_macos_privacy_settings("Privacy_Accessibility"))
        screen_recording_action = QAction("Open Screen Recording Settings", menu)
        screen_recording_action.triggered.connect(lambda: open_macos_privacy_settings("Privacy_ScreenCapture"))
        quit_action = QAction("Quit", menu)
        quit_action.triggered.connect(QApplication.instance().quit)
        menu.addAction(capture_action)
        menu.addAction(console_action)
        menu.addAction(history_action)
        menu.addAction(restart_action)
        if IS_MACOS:
            menu.addSeparator()
            menu.addAction(accessibility_action)
            menu.addAction(screen_recording_action)
        menu.addSeparator()
        menu.addAction(quit_action)
        return menu

    def _tray_activated(self, reason: QSystemTrayIcon.ActivationReason) -> None:
        if reason == QSystemTrayIcon.ActivationReason.DoubleClick:
            self.capture_and_analyze()

    def _icon(self) -> QIcon:
        pixmap = QPixmap(64, 64)
        pixmap.fill(QColor(0, 102, 255))
        painter = QPainter(pixmap)
        painter.setPen(QColor("white"))
        painter.drawText(pixmap.rect(), Qt.AlignmentFlag.AlignCenter, "AS")
        painter.end()
        return QIcon(pixmap)

    def open_console(self) -> None:
        if self.config_window is None:
            self.config_window = ConfigWindow(self.config, self.api)
            self.config_window.restart_requested.connect(self.restart_service)
            self.config_window.settings_saved.connect(self.reload_settings)
        self.config_window.show()
        self.config_window.raise_()

    def open_history(self) -> None:
        self._ensure_service()
        if self.history_window is None:
            self.history_window = HistoryWindow(self.api)
        self.history_window.show()
        self.history_window.raise_()

    def restart_service(self) -> None:
        try:
            self.service.restart()
            if self.config_window:
                self.config_window.refresh_service_status()
            QMessageBox.information(None, "AskShot", "Python service restarted.")
        except Exception as exc:
            QMessageBox.warning(None, "AskShot", f"Restart failed:\n{exc}")

    def capture_and_analyze(self) -> None:
        try:
            self._ensure_service()
            if IS_MACOS:
                if not has_screen_capture_access():
                    request_screen_capture_access()
                if not has_screen_capture_access():
                    raise RuntimeError(
                        "macOS 未允许屏幕录制权限。请在 System Settings -> Privacy & Security -> "
                        "Screen Recording 中允许当前 Python/Terminal 或 AskShot，然后重启 AskShot。"
                    )
                image_base64, image = self.capture.capture_interactive_region()
                self._start_analysis(image_base64, image, image.width(), image.height())
                return

            screenshot, origin = self.capture.capture()
            self.overlay = SelectionOverlay(screenshot, origin)
            self.overlay.selected.connect(self._region_selected)
            self.overlay.show()
            self.overlay.raise_()
            self.overlay.activateWindow()
        except Exception as exc:
            if isinstance(exc, CaptureCancelled):
                return
            message = str(exc)
            if "Screen Recording" in message or "屏幕录制" in message:
                box = QMessageBox()
                box.setWindowTitle("AskShot")
                box.setText("Capture failed")
                box.setInformativeText(message)
                settings_btn = box.addButton("Open Screen Recording", QMessageBox.ButtonRole.ActionRole)
                box.addButton(QMessageBox.StandardButton.Ok)
                box.exec()
                if box.clickedButton() == settings_btn:
                    open_macos_privacy_settings("Privacy_ScreenCapture")
            else:
                QMessageBox.warning(None, "AskShot", f"Capture failed:\n{exc}")

    def _region_selected(self, rect: QRect | None) -> None:
        self.overlay = None
        if rect is None:
            return
        try:
            image_base64, image = self.capture.crop_to_base64(rect)
            self._start_analysis(image_base64, image, rect.width(), rect.height())
        except Exception as exc:
            QMessageBox.warning(None, "AskShot", str(exc))

    def _start_analysis(self, image_base64: str, image: QImage, width: int, height: int) -> None:
        self.last_image_base64 = image_base64
        self.last_image = image
        self.last_hash = hashlib.md5(image_base64[:100].encode("utf-8")).hexdigest()[:8]
        if self.popup is not None:
            if self.popup.pinned:
                pinned_popup = self.popup
                self.pinned_popups.append(pinned_popup)
                pinned_popup.destroyed.connect(
                    lambda _=None, popup=pinned_popup: self._forget_pinned_popup(popup)
                )
            else:
                self.popup.close()
        self.popup = ResultPopup()
        self.popup.follow_up.connect(self.ask_follow_up)
        self.popup.show_text(f"Analyzing {width} x {height}...")
        self._analyze_async(image_base64, None, True)

    def _forget_pinned_popup(self, popup: ResultPopup) -> None:
        if popup in self.pinned_popups:
            self.pinned_popups.remove(popup)

    def ask_follow_up(self, question: str) -> None:
        if not self.last_image_base64:
            return
        self._analyze_async(self.last_image_base64, question, False)

    def _analyze_async(self, image_base64: str, question: str | None, save_record: bool) -> None:
        config = AppConfig.load()
        self.config = config

        def run() -> None:
            try:
                summary = self.api.analyze(image_base64, config, question)
                self.analysis_done.emit(summary, question or "", save_record)
            except Exception as exc:
                self.analysis_failed.emit(str(exc))

        threading.Thread(target=run, daemon=True).start()

    def _show_analysis(self, summary: str, question: str, save_record: bool) -> None:
        if self.popup is None:
            self.popup = ResultPopup()
            self.popup.follow_up.connect(self.ask_follow_up)
        if question:
            self.popup.append_text(summary)
        else:
            self.popup.show_text(summary)

        screenshot_path = None
        if save_record and self.config.data.save_screenshots and self.last_image is not None:
            screenshot_path = self._save_screenshot(self.last_image)
        if save_record:
            try:
                self.api.save_history(summary, question, screenshot_path, self.last_hash)
            except Exception:
                pass

    def _show_error(self, message: str) -> None:
        if self.popup:
            self.popup.append_text(f"Error: {message}")
        else:
            QMessageBox.warning(None, "AskShot", message)

    def _save_screenshot(self, image: QImage) -> str:
        directory = self.config.screenshots_dir()
        directory.mkdir(parents=True, exist_ok=True)
        path = directory / f"{datetime.now():%Y-%m-%d_%H%M%S}_{self.last_hash}.png"
        image.save(str(path), "PNG")
        return str(path)

    def _ensure_service(self) -> None:
        if not self.api.is_healthy():
            self.service.start()

    def reload_settings(self) -> None:
        self.config = AppConfig.load()
        self.hotkey.set_hotkey(self.config.hotkeys.capture_and_analyze)

    def shutdown(self) -> None:
        self.hotkey.stop()
        self.tray.hide()
        self.service.stop()


def main() -> int:
    APP_SUPPORT_DIR.mkdir(parents=True, exist_ok=True)
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    QApplication.setApplicationName("AskShot")
    QApplication.setOrganizationName("AskShot")
    app = QApplication(sys.argv)
    app.setQuitOnLastWindowClosed(False)
    if not QSystemTrayIcon.isSystemTrayAvailable():
        QMessageBox.warning(None, "AskShot", "系统托盘不可用，AskShot 将只显示控制台窗口。")
    controller = Controller()
    if not QSystemTrayIcon.isSystemTrayAvailable():
        controller.open_console()
    app.aboutToQuit.connect(controller.shutdown)
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
