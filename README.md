# ScreenMind · 智能截图分析工具

> 截图 → VLM → 答案，零 OCR，零 Docker，开箱即用。

ScreenMind 是一款轻量级的 Windows 桌面截图分析工具。你只需框选屏幕上的任意区域，它就会把截图直接发送给视觉语言模型（VLM）进行分析，并在屏幕右下角以悬浮窗形式返回结果。

**核心设计理念：** 截图直发视觉模型，不经过 OCR 中转；本地只有一个 C# 桌面客户端 + 一个 Python HTTP 代理，无数据库、无容器、无重型依赖。

---

## ✨ 功能特性

| 功能 | 说明 |
|---|---|
| **框选截图** | 热键唤起，鼠标拖动框选任意屏幕区域，支持多显示器/高 DPI |
| **VLM 直连** | 截图以 base64 PNG 直送视觉模型，不做本地 OCR |
| **多模型兼容** | 兼容 OpenAI 兼容协议（vLLM / Ollama / DeepSeek / Qwen / 其他云端 API） |
| **悬浮结果窗** | 右下角常驻结果展示，支持滚动和多轮追问 |
| **历史记录** | 每次分析自动写入 `data/history/*.json`，可按关键词检索 |
| **常驻托盘** | 系统托盘图标常驻，右键打开控制台 / 重启服务 / 退出 |
| **零数据库** | 历史记录以纯文本 JSON 文件持久化，易于迁移和备份 |
| **轻量后台** | Python 代理 ~20MB 内存，不加载任何模型 |

---

## 🚀 快速开始

### 方式一：从源码运行

```bash
# 1. 克隆仓库
git clone https://github.com/lqw905/screenmind.git
cd screenmind

# 2. 启动 Python 后端服务
cd services
pip install -r requirements.txt
python main.py
# 服务监听 http://127.0.0.1:8900

# 3. 编译并启动客户端（另开一个终端）
cd ../src/ScreenMind.Client
dotnet run -c Release
```

### 方式二：发布为单文件可执行程序

```bash
# 编译客户端
dotnet publish src/ScreenMind.Client/ScreenMind.Client.csproj \
  -c Release -o bin/publish --self-contained false

# 运行
bin/publish/ScreenMind.Client.exe
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
                │         ScreenMind.exe (C# WPF)        │
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
screenmind/
├── AGENTS.md                         # AI 助手指令（Codex 用）
├── CLAUDE.md                         # Claude 配置
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
└── src/ScreenMind.Client/            # C# WPF 桌面客户端
    ├── Models/AppConfig.cs           # 配置模型（LLM/Hotkey/General）
    ├── Services/
    │   ├── HotkeyService.cs          # 全局热键（RegisterHotKey）
    │   ├── ScreenCaptureService.cs   # GDI 截图 + DPI 坐标映射
    │   ├── PythonServiceManager.cs   # Python 子进程管理
    │   ├── InferenceClient.cs        # HTTP 客户端（单例 + 重试）
    │   └── TrayIconService.cs        # 系统托盘（WPF Popup 菜单）
    └── Views/
        ├── RegionSelector.xaml(.cs)  # 全屏选框（半透明遮罩）
        ├── ResultPopup.xaml(.cs)     # 右下角悬浮结果窗
        └── MainWindow.xaml(.cs)      # 控制台（LLM 配置 + 测试）
    ├── App.xaml(.cs)                 # 应用入口 + 全局异常处理
    └── ScreenMind.Client.csproj      # .NET 9 WPF 项目文件
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
dotnet build src/ScreenMind.Client/ScreenMind.Client.csproj -c Release

# 发布
dotnet publish src/ScreenMind.Client/ScreenMind.Client.csproj \
  -c Release -o bin/publish

# 启动 Python 服务（开发模式）
cd services && python -m uvicorn main:app --reload --host 127.0.0.1 --port 8900
```

### HTTP API

| 方法 | 路径 | 说明 |
|---|---|---|
| `GET` | `/health` | 健康检查，返回 `{"status":"ok"}` |
| `POST` | `/analyze` | 发送截图进行 VLM 分析 |
| `POST` | `/api/test` | 测试 LLM API 连通性（不发送截图） |
| `POST` | `/history/add` | 写入一条分析记录 |
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

---

## 📜 License

MIT — 欢迎修改和二次开发。
