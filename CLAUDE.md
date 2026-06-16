# CLAUDE.md

AskShot 项目的 AI 助手指令文件。本文档供 Claude Code 及其他 AI 编码助手使用。

## 项目概述

AskShot 是一款智能截图分析工具。截图直接发给 VLM（视觉语言模型），无需 OCR 中转。轻量、常驻后台、开箱即用。

## 核心架构原则

- **C# 做客户端**：截图 + 热键 + 托盘 + 控制台 + Python 进程管理
- **Python 做代理**：FastAPI 单进程 (:8900)，纯 HTTP 转发截图到 LLM，不加载任何模型
- **截图直发 VLM**：视觉模型原生理解画面内容，无需 OCR
- **C# 管理 Python 子进程**：System.Diagnostics.Process，隐藏窗口，自动重启
- **零数据库**：历史存 JSON 文件，关键词搜索匹配分析文本
- **NSIS 打包**：内嵌 Python embeddable，安装即用

## 技术栈

| 模块 | 技术 |
|---|---|
| 桌面客户端 | C# / .NET 9 WPF |
| Python 服务 | FastAPI + httpx（纯代理转发，不加载模型） |
| 视觉理解 | VLM 直接理解（用户自配 API：vLLM/Ollama/云端） |
| 历史存储 | JSON 文件（data/history/*.json） |
| 历史检索 | 纯文本关键词匹配 |
| 安装打包 | NSIS + Python embeddable |

## 项目结构

```
AskShot/
├── CLAUDE.md
├── docs/
│   ├── README.md
│   ├── architecture.md
│   ├── tech-stack.md
│   ├── mvp-roadmap.md
│   ├── latency-budget.md
│   └── rag-design.md
├── src/AskShot.Client/        # C# WPF 客户端
│   ├── Models/AppConfig.cs
│   ├── Services/
│   │   ├── PythonServiceManager.cs
│   │   └── InferenceClient.cs
│   ├── Views/
│   │   └── MainWindow.xaml(.cs)   # 控制台
│   └── App.xaml(.cs)
├── services/                     # Python 代理服务
│   ├── main.py                   # FastAPI :8900
│   ├── vlm_proxy.py              # 转发截图到 LLM
│   ├── history.py                # JSON 文件读写
│   ├── models.py
│   └── requirements.txt
├── data/                         # 运行时数据 (.gitignore)
│   ├── history/
│   ├── screenshots/
│   └── favorites.json
└── installer/                    # NSIS 打包脚本
```

## 关键设计要求

1. **截图直发 VLM**，不需要 OCR 预处理
2. Python 服务是纯代理，不加载任何 AI 模型
3. **不引入** Docker、数据库、OCR 引擎、向量引擎
4. C# HttpClient 单例复用
5. Python 进程端口固定 127.0.0.1:8900
6. 截图默认不保存，控制台提供开关和路径
