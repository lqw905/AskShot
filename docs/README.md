# ScreenMind — 方案文档索引

> 根目录的 [README.md](../README.md) 是项目的主说明文档，适合 GitHub 首页展示。以下文档是内部设计和技术细节。

## 文档索引

| 文档 | 内容 | 阅读时机 |
|---|---|---|
| [architecture.md](./architecture.md) | 整体架构设计、模块职责、数据流、安装与启动流程 | 理解系统设计 |
| [tech-stack.md](./tech-stack.md) | 核心技术栈选型与对比分析 | 评估技术选型 |
| [mvp-roadmap.md](./mvp-roadmap.md) | MVP 分步实施路径（含控制台、进程管理、NSIS 打包） | 规划开发路线 |
| [latency-budget.md](./latency-budget.md) | 延迟预算拆解与性能优化策略 | 性能分析与优化 |
| [rag-design.md](./rag-design.md) | 知识检索与记忆系统设计 | 理解历史检索机制 |

## 项目速览

- **桌面客户端**：C# / .NET 9 WPF，负责截图、热键、托盘、悬浮窗、Python 进程管理
- **后端代理**：Python FastAPI (:8900)，纯 HTTP 转发截图到 VLM，不加载模型
- **视觉理解**：截图直送 VLM，视觉模型原生理解画面内容，无需 OCR 中转
- **存储方案**：历史记录写入 `data/history/*.json`，零数据库
- **LLM 兼容性**：任何 OpenAI 兼容接口（vLLM / Ollama / DeepSeek / 云端 API）

## 运行数据流

```
用户按 Ctrl+Shift+A
    ↓
RegionSelector.xaml  →  全屏半透明遮罩，鼠标框选区域
    ↓
ScreenCaptureService  →  GDI CreateDC("DISPLAY") 捕获虚拟屏幕，按 DPI 映射裁切
    ↓
InferenceClient      →  POST /analyze (base64 PNG + 配置)
    ↓
main.py (:8900)      →  转发到 vlm_proxy.py
    ↓
vlm_proxy.py         →  httpx POST 到外部 LLM API
    ↓
LLM 返回分析结果     →  history.py 写入 data/history/*.json
    ↓
ResultPopup.xaml     →  右下角悬浮窗展示结果
```

## 配置文件

程序运行时会在可执行文件同目录生成 `appsettings.json`：

```json
{
  "llm": {
    "endpoint": "https://api.deepseek.com/v1",
    "api_key": "sk-xxx",
    "model": "deepseek-chat",
    "temperature": 0.7,
    "max_tokens": 1024
  },
  "hotkey": {
    "modifiers": 3,
    "key": 65
  },
  "general": {
    "save_screenshots": false,
    "screenshot_dir": "data/screenshots"
  }
}
```
