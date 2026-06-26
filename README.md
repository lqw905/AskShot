# AskShot · Ask anything on your screen

> 框选屏幕上的任何东西 → 视觉大模型直接回答你。零 OCR，零容器，开箱即用。

AskShot 是一款轻量级的桌面截图问答工具。Windows 端使用 C# / WPF，macOS 端使用 PySide6；你看到屏幕上不理解的内容，只需按 `Ctrl+Shift+A` 框选区域，它就会把截图直接发送给视觉语言模型（VLM），并在屏幕右下角以悬浮窗形式返回答案。

**核心设计理念：** 截图直发视觉模型，不经过 OCR 中转；本地只有一个 C# 桌面客户端 + 一个 Python HTTP 代理，无数据库、无容器、无重型依赖。

---

## 💡 为什么需要 AskShot？

上网、编程、看文档时，遇到不认识的按钮、不理解的界面、不确定的功能，第一反应是"这是什么"——但要得到答案，你需要：复制 → 切换窗口 → 粘贴 → 写 prompt → 等回答 → 切回来。这一套打断了多少次好奇心的出口，久而久之就懒得问了。

AskShot 解决的就是这个：**框选 → 问**，答案直接在右下角浮现，整个过程不到 3 秒，好奇心得到满足，不需要离开当前页面。

> 你看到屏幕上任何不理解的东西 → 按 `Ctrl+Shift+A` 框选它 → 答案就在右下角

---

## ✨ 功能特性

| 功能 | 说明 |
|---|---|
| **框选截图** | 热键唤起，鼠标拖动框选任意屏幕区域，支持多显示器/高 DPI |
| **VLM 直连** | 截图以 base64 PNG 直送视觉模型，不做本地 OCR |
| **多模型兼容** | 兼容 OpenAI 兼容协议（vLLM / Ollama / DeepSeek / Qwen / 其他云端 API） |
| **悬浮结果窗** | 右下角常驻结果展示，淡入动画，支持滚动和多轮追问 |
| **常驻托盘** | 系统托盘相机图标常驻，右键打开控制台 / 重启服务 / 退出 |
| **零数据库** | 历史记录以纯文本 JSON 文件持久化，易于迁移和备份 |
| **轻量后台** | Python 代理 ~20MB 内存，不加载任何模型 |

---

## 🚀 快速开始

### 方式一：从源码运行

#### Windows

```bash
# 1. 克隆仓库
git clone https://github.com/lqw905/AskShot.git
cd AskShot

# 2. 一键创建环境、安装后端依赖并启动 WPF 客户端
powershell -ExecutionPolicy Bypass -File scripts/run-windows.ps1
```

Windows 端会由 WPF 客户端自动管理 Python 后端服务，运行时配置和数据保存在：

```text
%APPDATA%\AskShot\
├── appsettings.json
├── data\history\
├── data\screenshots\
└── logs\
```

#### macOS

macOS 端复用同一个 Python 后端，客户端入口在 `src/AskShot.Mac/askshot_mac.py`：

```bash
# 1. 克隆仓库
git clone https://github.com/lqw905/AskShot.git
cd AskShot

# 2. 一键创建环境、安装依赖并启动 macOS 客户端
./scripts/run-macos.sh
```

macOS 首次使用需要在系统设置里允许：

- **Accessibility / 辅助功能**：用于全局快捷键监听。
- **Screen Recording / 屏幕录制**：用于截图捕获。

macOS 运行时配置和数据保存在：

```text
~/Library/Application Support/AskShot/
├── appsettings.mac.json
├── data/history/
├── data/screenshots/
└── logs/
```

### 方式二：发布为单文件可执行程序

```bash
# 编译客户端
dotnet publish src/AskShot.Client/AskShot.Client.csproj \
  -c Release -o bin/publish --self-contained false

# 运行
bin/publish/AskShot.Client.exe
```

---

## ⚙️ 配置 LLM API

启动程序后，**右键系统托盘图标 → 控制台**，在控制台中填入你的 VLM API 信息：

| 配置项 | 示例 | 说明 |
|---|---|---|
| **Endpoint** | `https://api.deepseek.com/v1` | OpenAI 兼容接口的 base URL，路径需包含 `/v1` |
| **API Key** | `sk-xxxxxxxxxxxxxxxx` | 你的 API 密钥 |
| **Model** | `deepseek-chat` 或 `qwen2.5-vl-3b-instruct` | 视觉/对话模型名称 |
| **Temperature** | `0.3 ~ 1.0` | 控制生成内容的随机性 |
| **Max Tokens** | `512 ~ 2048` | 单次返回的最大 token 数 |

> 💡 **提示：** 如果使用本地模型（如 Ollama + qwen2.5-vl），Endpoint 填 `http://127.0.0.1:11434/v1`，API Key 可留空。

### 常用 Endpoint 参考

| 服务 | Endpoint | Model 示例 |
|---|---|---|
| DeepSeek | `https://api.deepseek.com/v1` | `deepseek-chat` |
| Ollama (本地) | `http://127.0.0.1:11434/v1` | `qwen2.5-vl` |
| vLLM (本地) | `http://127.0.0.1:8000/v1` | `qwen2.5-vl-7b-instruct` |
| 其他 OpenAI 兼容 | 对应服务的 `/v1` 路径 | 对应模型名 |

---

## 🎮 使用方式

| 操作 | 效果 |
|---|---|
| 按 `Ctrl + Shift + A` | 全屏变暗，拖动鼠标框选要分析的区域 |
| 松开鼠标 | 截图自动发送给 VLM，右下角悬浮窗显示结果 |
| 右键系统托盘图标 | 打开控制台 / 重启服务 / 退出程序 |
| 双击系统托盘图标 | 立即触发截图分析 |

---

## 🏗️ 架构总览

```
                ┌─────────────────────────────────────────┐
                │         AskShot.exe (C# WPF)        │
                │                                         │
                │  ┌──────────┐  ┌──────────┐  ┌───────┐ │
                │  │ 热键服务  │  │ 截图捕获  │  │ 托盘  │ │
                │  │(RegisterHotKey)│(GDI BitBlt) │       │ │
                │  └──────────┘  └──────────┘  └───┬───┘ │
                │                                    │     │
                │  ┌──────────┐  ┌──────────┐        │     │
                │  │ 悬浮结果  │  │ 控制台   │        │     │
                │  │ (悬浮窗)  │  │ (MainWindow) │        │     │
                │  └──────────┘  └──────────┘        │     │
                │                                    │     │
                │  ┌──────────────────────────────────┘     │
                │  │  Python 进程管理器 (System.Diagnostics)  │
                │  │  启动/健康检查/异常重启/日志捕获         │
                │  └──────────────────┬──────────────────────┘
                │                     │  HTTP (localhost:8900)
                │                     ▼
                │  ┌─────────────────────────────────────────┐
                │  │       InferenceClient (HttpClient)      │
                │  │  ┌─ POST /analyze (base64截图 + 配置)  │
                │  │  ├─ GET  /health (健康检查)             │
                │  │  ├─ POST /history/add                  │
                │  │  └─ POST /history/search               │
                │  └──────────────────┬──────────────────────┘
                │                     │
                └─────────────────────┼──────────────────────┘
                                      │
          ┌───────────────────────────┼───────────────────────────┐
          ▼                           ▼                           ▼
  ┌──────────────────┐      ┌──────────────────┐        ┌──────────────────┐
  │ main.py          │      │ vlm_proxy.py     │        │ 外部 LLM API     │
  │ FastAPI :8900    │──────▶ httpx POST       │───────▶ 视觉语言模型     │
  │ 路由 + 生命周期   │      │ OpenAI 兼容格式  │        │ (OpenAI / vLLM   │
  └──────────────────┘      └──────────────────┘        │  Ollama / DeepSeek)
          │                                               └──────────────────┘
          ▼
  ┌──────────────────┐
  │ history.py       │
  │ data/history/    │
  │ *.json 读写      │
  └──────────────────┘
```

### 关键设计决策

1. **单截图源原则**：`CreateDC("DISPLAY")` 一次性捕获整个虚拟屏幕（跨多显示器），坐标映射用 `mouse_DIP × DPI_Scale = pixel`，从同一张位图裁切选区——避免高 DPI 环境下选框与截图错位。
2. **C# 主进程，Python 子进程**：C# 负责 Windows 桌面交互（热键、GDI 截图、WPF UI），Python 负责 HTTP 代理转发和 JSON 历史。通过 `System.Diagnostics.Process` 管理，窗口隐藏、异常自动重启。
3. **零数据库**：历史记录写入 `data/history/*.json`，检索用纯文本关键词匹配，无需数据库引擎。
4. **配置即 JSON**：LLM 配置保存在 `appsettings.json`（与可执行文件同目录），便于迁移和备份。

---

## 📁 项目结构

```
AskShot/
├── AGENTS.md                         # AI 助手指令（Codex 用）
├── CLAUDE.md                         # Claude 配置
├── AskShot.ico                       # 应用程序图标
├── docs/
│   ├── README.md                     # 详细方案文档索引
│   ├── architecture.md               # 整体架构设计
│   ├── tech-stack.md                 # 技术栈选型
│   ├── mvp-roadmap.md                # MVP 实施路径
│   ├── latency-budget.md             # 延迟预算与优化
│   └── rag-design.md                 # 知识检索与记忆系统
├── services/                         # Python 后端服务
│   ├── main.py                       # FastAPI 入口（:8900）
│   ├── vlm_proxy.py                  # VLM 代理（httpx 转发截图）
│   ├── history.py                    # JSON 文件历史记录
│   ├── models.py                     # Pydantic 请求/响应模型
│   └── requirements.txt              # fastapi + uvicorn + httpx
├── scripts/
│   ├── package-windows.ps1           # Windows 打包脚本
│   ├── package-macos.sh              # macOS 打包脚本
│   ├── run-macos.sh                  # macOS 开发启动脚本
│   └── run-windows.ps1               # Windows 开发启动脚本
├── .github/workflows/
│   └── release.yml                   # GitHub Actions 自动打包发布
└── src/AskShot.Client/            # C# WPF 桌面客户端
    ├── Models/AppConfig.cs           # 配置模型（LLM/Hotkey/Data）
    ├── Services/
    │   ├── HotkeyService.cs          # 全局热键（RegisterHotKey）
    │   ├── ScreenCaptureService.cs   # GDI 截图 + DPI 坐标映射
    │   ├── PythonServiceManager.cs   # Python 子进程管理
    │   ├── InferenceClient.cs        # HTTP 客户端（单例 + 重试）
    │   └── TrayIconService.cs        # 系统托盘（相机图标 + WPF Popup 菜单）
    ├── Views/
    │   ├── RegionSelector.xaml(.cs)  # 全屏选框（半透明遮罩）
    │   ├── ResultPopup.xaml(.cs)     # 右下角悬浮结果窗（淡入动画）
    │   ├── HistoryWindow.xaml(.cs)   # 历史记录浏览窗口
    │   └── MainWindow.xaml(.cs)      # 控制台（Claude 设计系统样式）
    ├── App.xaml(.cs)                 # 应用入口 + 全局异常处理 + 设计系统资源
    └── AskShot.Client.csproj         # .NET 9 WPF 项目文件
└── src/AskShot.Mac/               # macOS PySide6 客户端
    ├── askshot_mac.py             # 托盘 / 热键 / 截图框选 / 配置 / 历史搜索
    └── requirements.txt           # macOS 客户端 + 后端依赖
```

---

## 🔧 开发环境

| 依赖 | 版本要求 |
|---|---|
| .NET SDK | 9.0+ |
| Python | 3.9+ |
| Windows | 10 / 11（WPF 应用） |

### 构建命令

```bash
# 编译 C# 客户端
dotnet build src/AskShot.Client/AskShot.Client.csproj -c Release

# 发布
dotnet publish src/AskShot.Client/AskShot.Client.csproj \
  -c Release -o bin/publish

# 启动 Python 服务（开发模式）
cd services && python -m uvicorn main:app --reload --host 127.0.0.1 --port 8900
```

### 发布打包

GitHub Actions 会在推送 `v*` tag 或手动运行 Release workflow 时打包：

```bash
git tag v0.1.0
git push origin v0.1.0
```

产物：

```text
AskShot-0.1.0-macos-x64.zip      # AskShot.app + 内置 Python 代理服务
AskShot-0.1.0-windows-x64.zip    # WPF portable build + 内置 Python 代理服务（含应用图标）
```

本地打包命令：

```bash
# macOS
VERSION=0.1.0 scripts/package-macos.sh
```

```powershell
# Windows
$env:VERSION = "0.1.0"
powershell -ExecutionPolicy Bypass -File scripts/package-windows.ps1
```

### HTTP API

| 方法 | 路径 | 说明 |
|---|---|---|
| `GET` | `/health` | 健康检查，返回 `{"status":"ok"}` |
| `POST` | `/analyze` | 发送截图进行 VLM 分析 |
| `POST` | `/config/test` | 测试 LLM API 连通性（不发送截图） |
| `POST` | `/history/save` | 写入一条分析记录 |
| `POST` | `/history/search` | 按关键词检索历史 |

`/analyze` 请求体示例：

```json
{
  "image_base64": "iVBORw0KGgoAAAANSU...",
  "text": "请分析这张截图的内容",
  "api_config": {
    "endpoint": "https://api.deepseek.com/v1",
    "api_key": "sk-xxx",
    "model": "deepseek-chat",
    "temperature": 0.7,
    "max_tokens": 1024
  }
}
```

---

## 🐛 排错指南

| 现象 | 可能原因 | 解决方式 |
|---|---|---|
| 截图框选区域与实际截图错位 | 多显示器 + 高 DPI 下坐标系统不一致 | 已修复（单截图源 + DPI 映射），如仍有问题请反馈 |
| 分析结果为 "Mock Response" | LLM 配置为空或请求失败 | 在控制台填入正确的 Endpoint/API Key/Model |
| 程序启动即崩溃 | 字体缓存损坏或配置文件损坏 | 删除 `bin/publish/appsettings.json` 后重启 |
| Python 服务无法启动 | `python` 不在 PATH，或端口被占用 | 确认 `python --version` 可用，端口 8900 无冲突 |
| 控制台报 HTTP 500 | Python 服务有异常 | 查看 `bin/publish/` 下的日志文件 |
| Windows 历史/配置找不到 | 数据迁移到了用户目录 | 查看 `%APPDATA%\AskShot\` |
| macOS 快捷键无响应 | 未授予辅助功能权限 | 控制台 General 页打开 Accessibility，允许运行 AskShot 的终端或打包 App |
| macOS 截图为空或黑屏 | 未授予屏幕录制权限 | 控制台 General 页打开 Screen Recording，允许运行 AskShot 的终端或打包 App 后重启 |
| macOS 后端启动失败 | 服务日志有异常 | 查看 `~/Library/Application Support/AskShot/logs/` |

---

## 📜 License

MIT — 欢迎修改和二次开发。
