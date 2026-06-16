# 核心技术栈选型

## 总览

| 模块 | 技术选型 | 选型理由 |
|---|---|---|
| 桌面客户端 | C# / .NET 9 WPF（评估 Greenshot） | 复用成熟截图能力，聚焦 AI 差异化 |
| 视觉理解 | VLM 直接理解截图 | 截图发给视觉模型，原生理解画面，无需 OCR |
| Python 服务 | FastAPI 代理转发 | 纯 HTTP 转发，不加载模型，极轻量 |
| Python 进程管理 | System.Diagnostics.Process（C# 侧） | 零额外依赖，隐藏窗口，异常自动重启 |
| 历史存储 | JSON 文件 | 最简单，零依赖 |
| 历史检索 | 纯文本关键词匹配 | 匹配 VLM 返回的分析文本 |
| 安装打包 | NSIS | 内嵌 Python embeddable，开箱即用 |

---

## 桌面客户端基座

### 方案 A：基于 Greenshot 二次开发（推荐优先评估）

**优势**：
- 轻量、稳定、代码清晰的老牌开源截图工具
- 完整的截图捕获、编辑标注、快捷键交互能力
- 社区活跃，文档丰富

**风险**：
- 主仓库仍为 .NET Framework 4.8，.NET Core 迁移（GreenshotNext）进度不确定
- .NET Framework 4.8 调现代化 HTTP 服务较别扭
- 需要验证迁移分支的完成度和稳定性

**评估清单**：
- [ ] 确认 GreenshotNext 的 .NET Core 迁移进度
- [ ] 验证编译通过，截图功能正常
- [ ] 评估是否可以替换/扩展结果展示模块

### 方案 B：自研轻量截图模块（备选）

如果 Greenshot 迁移不理想，基于 Windows.Graphics.Capture API 自研：

- 截图捕获：~200 行代码
- 全局热键：RegisterHotKey Win32 API，~100 行
- 基础编辑（箭头、矩形、文字）：~500 行
- 总工作量可控，且能完全掌控截图画质和格式转换

---

## 视觉语言模型 (VLM)

不内置任何模型。Python 服务作为代理，将截图转发到用户配置的 OpenAI 兼容 API。

### 用户可选方案

| 方案 | 示例 | 需要 |
|---|---|---|
| 本地 vLLM | `http://localhost:8080/v1` | 用户自行启动 vLLM 加载模型 |
| 本地 llama.cpp | `http://localhost:8081/v1` | 用户自行启动 llama-server |
| 本地 Ollama | `http://localhost:11434/v1` | 用户自行安装 Ollama |
| 局域网 GPU 服务器 | `http://192.168.1.100:8080/v1` | 家庭/办公室共享 GPU |
| 云端 API | `https://api.openai.com/v1` | API Key |

### 推荐模型（供用户参考）

| 模型 | 参数量 | 显存需求 | 中文支持 | 适用场景 |
|---|---|---|---|---|
| Qwen2.5-VL-3B | 3B | ~6GB | ★★★★★ | 通用截图理解 |
| InternVL3-2B | 2B | ~4GB | ★★★★★ | 文档/UI截图专项优化 |
| Gemma 4 4B | 4B | ~7GB | ★★★☆ | 代码/报错截图 |

---

## 历史存储与检索

### MVP：纯文件方案

```
data/
├── history/        # 每次分析存一个 JSON（~3-10KB/条）
├── screenshots/    # 可选：截图原图
└── favorites.json  # 收藏 ID 列表
```

检索 VLM 返回的分析文本，不是截图内的文字。关键词搜索扫描 JSON 文件即可。

---

## 不推荐的技术方向

| 方向 | 理由 |
|---|---|
| OCR 预处理 | VLM 原生理解图片，OCR 是多余步骤 |
| Greenshot + .NET Framework 4.8 | 技术债重，后续集成受限 |
| 云端 API 作为唯一方案 | 违背隐私优先原则 |
| 大参数 VLM (7B+) 内置捆绑 | 用户机器带不动，应让用户自选 |
| Docker Desktop | 太重，2-4GB 额外内存，启动慢 |
| ChromaDB / 向量数据库 | MVP 数据量小，杀鸡用牛刀 |
