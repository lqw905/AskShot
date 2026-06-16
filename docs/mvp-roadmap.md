# MVP 实施路径

## 整体阶段规划

```
Phase 0: 环境搭建          (1-2 天)
Phase 1: 截图基座          (2-3 天)
Phase 2: Python 推理服务   (3-4 天)   ← 合并 OCR + VLM代理 + RAG
Phase 3: 控制台 + 进程管理 (2-3 天)   ← 新增
Phase 4: 悬浮窗 + 交互     (3-4 天)
Phase 5: 打包安装 + 联调   (2-3 天)
─────────────────────────────────
MVP 总计预估               13-19 天
```

---

## Phase 0：环境搭建 (Day 1-2)

### 0.1 C# 开发环境

```
- 安装 Visual Studio 2022 Community 或 VS Code + C# Dev Kit
- 安装 .NET 8.0 SDK
- Git clone Greenshot 源码 & GreenshotNext 分支（用于评估）
```

### 0.2 Python 推理环境

```
- 下载 Python 3.11 embeddable (用于内嵌打包，~30MB)
- pip install paddleocr fastapi uvicorn chromadb sentence-transformers
- 开发阶段直接用系统 Python，打包时切 embeddable
```

### 0.3 模型下载

```bash
# PaddleOCR 模型 (~10MB)
# PP-OCRv3 检测 + 识别模型

# Embedding 模型 (~80MB)
# sentence-transformers/all-MiniLM-L6-v2

# VLM 模型由用户自行配置（控制台填入 API 地址）
# 推荐：Qwen2.5-VL-3B 或 InternVL3-2B
```

### 交付物
- [ ] C# 项目能编译运行
- [ ] Python 服务能在命令行启动并响应 health check
- [ ] 模型文件下载到本地

---

## Phase 1：截图基座搭建 (Day 3-5)

### 1.1 评估 Greenshot 可行性

```csharp
// 关键验证点：
// 1. GreenshotNext 分支是否可编译为 .NET Core 项目
// 2. 截图核心流程（框选→捕获→保存）是否正常
// 3. 全局热键注册是否工作
// 4. 结果展示模块是否可扩展/替换
```

### 1.2 决策分支

```
Greenshot .NET Core 迁移可用？
├── YES → 基于 Greenshot 开发，修改结果输出模块
│         - 截取截图完成后，将 Bitmap 转为 base64
│         - 发送到本地推理服务
│         - 将返回结果展示在自定义悬浮窗
│
└── NO  → 自研轻量截图模块
          - Windows.Graphics.Capture API 实现截图
          - RegisterHotKey 实现全局热键
          - 基础编辑标注（箭头、矩形、文字）
```

### 1.3 托盘图标基础框架

```csharp
// 托盘图标 + 右键菜单（MVP 最小可用版）
var trayIcon = new 
{
    Icon = Resources.AppIcon,
    Visible = true,
    Text = "ScreenMind",
    ContextMenuStrip = new ContextMenuStrip
    {
        Items =
        {
            new ToolStripMenuItem("控制台", null, OpenConsole),
            new ToolStripMenuItem("截图分析", null, CaptureAndAnalyze),
            new ToolStripMenuItem("-"),
            new ToolStripMenuItem("退出", null, Exit),
        }
    }
};
```

### 交付物
- [ ] 截图功能正常工作（全局热键触发 + 框选 + 捕获）
- [ ] 托盘图标 + 右键菜单可用
- [ ] Bitmap → base64 → HTTP 调用 → 解析响应 通路调通

---

## Phase 2：Python 推理服务 (Day 6-9)

### 2.1 单服务架构

```
services/
├── main.py          # FastAPI 入口，lifespan 中加载 OCR 模型
├── ocr_engine.py    # PaddleOCR 封装
├── vlm_proxy.py     # 转发到用户配置的 LLM API（OpenAI 兼容格式）
├── history.py       # 文件存储：JSON 读写 + 关键词搜索
├── models.py        # Pydantic models
└── requirements.txt
```

### 2.2 main.py 骨架

```python
# services/main.py
from fastapi import FastAPI
from contextlib import asynccontextmanager
import uvicorn

from ocr_engine import OcrEngine
from vlm_proxy import VlmProxy

ocr: OcrEngine = None
vlm: VlmProxy = None

@asynccontextmanager
async def lifespan(app: FastAPI):
    global ocr, vlm
    ocr = OcrEngine(lang='ch', use_gpu=True)
    vlm = VlmProxy()  # 运行时从请求中读取 API 配置
    yield

app = FastAPI(title="ScreenMind", lifespan=lifespan)

@app.get("/health")
async def health():
    return {
        "status": "ok",
        "ocr_loaded": ocr is not None,
        "vlm_ready": await vlm.check_ready(),
    }

@app.post("/ocr")
async def extract_text(request: OcrRequest):
    return await ocr.extract(request.image_base64)

@app.post("/analyze")
async def analyze(request: AnalyzeRequest):
    ocr_text = await ocr.extract(request.image_base64) if request.image_base64 else None
    return await vlm.analyze(
        image_base64=request.image_base64,
        ocr_text=ocr_text,
        user_question=request.user_question,
        api_config=request.api_config,
    )

# --- 历史记录接口（纯文件，零依赖）---
@app.post("/history/save")
async def save_history(item: HistoryItem):
    """写入 data/history/{id}.json"""
    ...

@app.get("/history/recent")
async def get_recent(limit: int = 10, hours: int = 24):
    """按文件时间戳排序，取最近 N 条"""
    ...

@app.post("/history/search")
async def search_history(request: SearchRequest):
    """关键词匹配搜索"""
    ...

if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=8900)
```

### 2.3 vlm_proxy.py — LLM API 代理

```python
# 关键设计：不绑定任何特定模型，纯代理转发
# 用户通过控制台传入 API 配置，每个请求带 api_config

class VlmProxy:
    async def analyze(self, image_base64, ocr_text, user_question,
                      system_prompt, api_config: ApiConfig):
        # 构造 OpenAI 兼容的 messages
        messages = [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": [
                {"type": "image_url", "image_url": {"url": f"data:image/png;base64,{image_base64}"}},
                {"type": "text", "text": f"OCR文本: {ocr_text}\n用户问题: {user_question}"}
            ]}
        ]

        async with httpx.AsyncClient(timeout=30) as client:
            resp = await client.post(
                f"{api_config.endpoint}/chat/completions",
                headers={"Authorization": f"Bearer {api_config.api_key}"},
                json={
                    "model": api_config.model,
                    "messages": messages,
                    "temperature": api_config.temperature,
                    "max_tokens": api_config.max_tokens,
                }
            )
            return resp.json()
```

### 交付物
- [ ] FastAPI 服务在 :8900 正常响应
- [ ] OCR、VLM 代理、历史保存/搜索 四个核心接口可用
- [ ] JSON 文件历史记录读写正常
- [ ] Health check 返回正确状态
- [ ] 单次 OCR < 500ms，VLM 代理转发 < 100ms（不含模型推理时间）

---

## Phase 3：控制台 + 进程管理 (Day 10-12)

### 3.1 Python 进程管理器

```csharp
public class PythonServiceManager : IDisposable
{
    private Process? _process;
    private readonly string _pythonExe;   // "python\python.exe"
    private readonly string _serviceDir;  // "services\"
    private const int Port = 8900;

    public async Task StartAsync()
    {
        if (await IsHealthy()) return;

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _pythonExe,
                Arguments = "main.py",
                WorkingDirectory = _serviceDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
            EnableRaisingEvents = true,
        };

        _process.Exited += OnProcessExited;
        _process.Start();

        // 等待就绪
        await WaitForReady(TimeSpan.FromSeconds(30));
    }

    private async Task<bool> IsHealthy()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var resp = await http.GetAsync($"http://127.0.0.1:{Port}/health");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task WaitForReady(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (await IsHealthy()) return;
            await Task.Delay(500);
        }
        throw new TimeoutException("Python 服务启动超时");
    }

    private async void OnProcessExited(object? sender, EventArgs e)
    {
        // 异常退出自动重启（退避策略）
        await Task.Delay(2000);
        await StartAsync();
    }

    public void Stop()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill();
        }
    }

    public void Dispose() => Stop();
}
```

### 3.2 控制台配置窗口

```
控制台 Tab 页设计：

┌─ LLM 配置 ─┬─ OCR ─┬─ 快捷键 ─┬─ 数据 ─┬─ 服务状态 ─┐
│                                                       │
│  推理模式:  ● 本地 API    ○ 云端 API                  │
│                                                       │
│  API 地址:   [http://localhost:8080/v1           ]    │
│  API Key:    [••••••••••••••••                 👁]    │
│  模型名称:   [qwen2.5-vl-3b-instruct            ]    │
│  Temperature:[0.7                          ───○]    │
│  Max Tokens: [2048                         ───○]    │
│                                                       │
│  [测试连接]  ● 连接成功，模型: qwen2.5-vl-3b         │
│                                                       │
└───────────────────────────────────────────────────────┘
```

### 3.3 配置持久化

```csharp
// 使用 appsettings.json 存储配置
public class AppConfig
{
    public LlmConfig Llm { get; set; } = new();
    public OcrConfig Ocr { get; set; } = new();
    public HotkeyConfig Hotkeys { get; set; } = new();
    public DataConfig Data { get; set; } = new();
}

public class LlmConfig
{
    public string ApiEndpoint { get; set; } = "http://localhost:8080/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "qwen2.5-vl-3b-instruct";
    public float Temperature { get; set; } = 0.7f;
    public int MaxTokens { get; set; } = 2048;
}

public class DataConfig
{
    public bool SaveScreenshots { get; set; } = false;    // 默认不保存截图（省空间）
    public string ScreenshotPath { get; set; } = "";       // 空 = data/screenshots/
    public int HistoryRetentionDays { get; set; } = 30;
}
```

### 3.4 控制台截图保存设置

```
┌─ 数据 ────────────────────────────────────────────┐
│                                                    │
│  ☐ 保存截图原图                                    │
│    截图保存路径: [C:\Users\xxx\Pictures\ScreenMind 📁]│
│                                                    │
│  历史记录保留: [30 天                        ───○]│
│  当前占用: 12 MB (142 条记录 + 0 张截图)          │
│                                                    │
│     [清空历史记录]   [导出备份]                    │
│                                                    │
└────────────────────────────────────────────────────┘
```

> 默认关闭截图保存。用户开启后才保存 PNG 到指定路径，避免硬盘膨胀。

### 交付物
- [ ] 控制台窗口完整可用（5 个 Tab）
- [ ] LLM 连接测试通过
- [ ] 配置持久化读写正常
- [ ] Python 服务启动/停止/重启稳定
- [ ] 异常退出自动恢复验证通过

---

## Phase 4：悬浮窗 + 用户交互 (Day 13-16)

### 4.1 悬浮窗 UI

```csharp
// WPF 悬浮窗设计
// - 无边框窗口，始终置顶 (Topmost = true)
// - 半透明暗色背景，毛玻璃效果 (AcrylicBrush)
// - 分析结果 Markdown 渲染区
// - 底部追问输入框
// - ⭐ 收藏 / 📋 复制 / 🔗 历史记录 快捷按钮
```

### 4.2 交互流程

```
场景 1：首次截图分析
  Ctrl+Shift+A → 框选 → 松开鼠标
    → OCR (C#→Python :8900)
    → VLM 分析 (Python→LLM API)
    → 悬浮窗弹出展示结果
    → 后台索引存储

场景 2：追问历史
  悬浮窗底部输入 "刚才那个报错怎么解决？"
    → RAG 检索最近记录
    → 拼接上下文送 VLM
    → 原地更新回答

场景 3：浏览历史
  托盘 → 搜索历史 → 关键词检索 → 点击查看完整分析
```

### 4.3 截图后处理管道

```csharp
public async Task<AnalysisResult> ProcessScreenshot(Bitmap screenshot)
{
    var base64 = ToBase64(screenshot);

    // 并行：OCR + 图片预处理
    var ocrTask = _client.PostAsJsonAsync("/ocr", new { image = base64 });
    var preprocessTask = PreprocessImage(screenshot); // 裁剪+缩放

    await Task.WhenAll(ocrTask, preprocessTask);

    var ocrResult = await ocrTask.Result.Content.ReadFromJsonAsync<OcrResult>();

    // VLM 分析（使用预处理后的图片）
    var analyzeResp = await _client.PostAsJsonAsync("/analyze", new
    {
        image_base64 = ToBase64(preprocessTask.Result),
        ocr_text = ocrResult.Text,
        api_config = _config.Llm,  // 当前控制台配置
    });

    var result = await analyzeResp.Content.ReadFromJsonAsync<AnalysisResult>();

    // 后台索引，不阻塞展示
    _ = _client.PostAsJsonAsync("/index", new
    {
        text = result.Summary,
        metadata = new { timestamp = DateTime.UtcNow, ocr_snippet = ocrResult.Text[..200] }
    });

    return result;
}
```

### 交付物
- [ ] 悬浮窗展示 + 追问交互可用
- [ ] 截图到结果完整链路调通
- [ ] 历史检索功能可用

---

## Phase 5：打包安装 + 联调优化 (Day 17-19)

### 5.1 NSIS 安装包

```nsis
; ScreenMind-Setup.nsi
; 打包内容：
;   - ScreenMind.exe (C# publish 单文件)
;   - python/ (embeddable Python + site-packages)
;   - services/ (Python 代码)
;   - models/ (OCR + Embedding 模型)
;   - data/ (空目录，运行时使用)
;   - 注册表：开机自启
```

```bash
# 构建命令
dotnet publish src/ScreenMind.Client -c Release -r win-x64 --self-contained
makensis installer/ScreenMind-Setup.nsi
```

### 5.2 端到端优化

| 优化项 | 预期收益 |
|---|---|
| OCR INT8 量化 | -60% 推理时间 |
| VLM 输入分辨率降低 + 区域裁剪 | -30~50% token 数 |
| OCR 与图片预处理并行 | -100ms |
| Python 服务模型预热（常驻内存） | 消除首次加载延迟 |
| C# HttpClient 连接池复用 | -50ms |

### 5.3 验收清单

- [ ] NSIS 安装包能正常安装/卸载
- [ ] 安装后无需任何配置即可使用（Python 内嵌，开箱即用）
- [ ] 开机自启 + 托盘常驻正常
- [ ] 截图 → 结果展示延迟 < 5 秒
- [ ] 历史检索延迟 < 1 秒
- [ ] 连续常驻 24 小时无内存泄漏
- [ ] Python 服务异常退出后 5 秒内自动恢复
- [ ] 控制台配置修改后即时生效
- [ ] 截图保存开关正常，路径可自定义
- [ ] 关键词搜索历史功能正常
