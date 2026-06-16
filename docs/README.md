# ScreenMind — 智能截图分析工具 方案文档

## 项目定位

站在成熟开源截图工具的肩上，集成本地 OCR 与视觉模型，优先打磨从截图到结果的流畅体验。

## 核心设计原则

- **C# 做客户端**：截图 + 热键 + 托盘 + 控制台 + Python 进程管理
- **截图直发 VLM**：视觉模型原生理解画面，无需 OCR
- **Python 做代理**：FastAPI :8900，纯 HTTP 转发，不加载模型
- **零依赖**：无 Docker、无数据库、无 OCR 引擎
- **安装即用**：NSIS 打包，内嵌 Python embeddable

## 文档索引

| 文档 | 内容 |
|---|---|
| [architecture.md](./architecture.md) | 整体架构设计、模块职责、数据流、安装与启动流程 |
| [tech-stack.md](./tech-stack.md) | 核心技术栈选型与对比分析 |
| [mvp-roadmap.md](./mvp-roadmap.md) | MVP 分步实施路径（含控制台、进程管理、NSIS 打包） |
| [latency-budget.md](./latency-budget.md) | 延迟预算拆解与性能优化策略 |
| [rag-design.md](./rag-design.md) | 知识检索与记忆系统设计 |

## 快速理解

```
用户开机 → ScreenMind.exe 托盘自启
              │
              ├── 自动拉起 Python 推理服务 (隐藏窗口)
              │      └── PaddleOCR + VLM代理 → :8900
              │      └── 历史记录 → data/history/*.json
              │
              ├── 用户按热键 → 框选截图 → OCR → VLM → 悬浮窗展示
              │
              └── 右键托盘 → 控制台 → 配置 LLM API / 快捷键 / 截图保存
```

## 目标体验

- 截图到首次结果：**2~5 秒**
- 历史相关问题检索：**< 1 秒**
- 后台常驻内存：**~2GB**（OCR 模型），无 Docker/ChromaDB 额外开销
- 全离线/灵活联网：本地模型或云端 API 自由切换
