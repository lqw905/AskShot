# 延迟预算与性能优化

## 目标

- **首次截图分析**：2~5 秒（端到端：热键按下 → 结果展示）
- **历史追问**：< 1 秒（文本输入 → 答案展示）

---

## 延迟链路拆解

### 首次分析链路

```
┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│  截图捕获    │ → │  OCR 推理    │ → │  VLM 推理    │ → │  结果渲染    │ → │  向量存储    │
│  50-100ms   │    │  200-500ms  │    │  1.5-3s     │    │  50-100ms   │    │  50-100ms   │
└─────────────┘    └─────────────┘    └─────────────┘    └─────────────┘    └─────────────┘
       │                  │                  │                  │                  │
       │                  │                  │                  │                  │
       ▼                  ▼                  ▼                  ▼                  ▼
   编码为 PNG       PP-OCRv3 INT8     Qwen2.5-VL-3B      WPF 悬浮窗       Embedding +
   转为 base64      + TensorRT        720p 输入          文本排版       ChromaDB 写入
```

### 逐段预算

| 环节 | 乐观 (ms) | 典型 (ms) | 悲观 (ms) | 优化手段 |
|---|---|---|---|---|
| 截图捕获 + 编码 | 50 | 80 | 150 | Windows.Graphics.Capture 硬件加速；PNG 编码用原生 API |
| 图片传输 (C#→OCR) | 10 | 30 | 100 | localhost, 压缩 base64, 长连接复用 |
| OCR 推理 | 150 | 300 | 500 | INT8 量化 + TensorRT；限制输入 960px |
| OCR 结果传输 | 5 | 10 | 30 | 只传文本，不回传图片 |
| 图片预处理 (裁剪+缩放) | 20 | 50 | 100 | 用 NVENC 或 PIL 高效实现，在 VLM 服务侧完成 |
| 图片传输 (C#→VLM) | 20 | 50 | 150 | 缩放后传输，720p 图片 |
| VLM 推理 (3B 模型) | 1000 | 2000 | 3500 | **主要瓶颈**；降低分辨率；token 限制；prompt 精简 |
| 结果传输 | 5 | 10 | 30 | 文本量小 |
| 结果渲染 | 30 | 60 | 100 | 虚拟化 UI，避免阻塞主线程 |
| Embedding + 存储 | 30 | 80 | 150 | 异步写入，不阻塞结果展示 |
| **总计** | **~1.3s** | **~2.7s** | **~4.8s** | |

---

## 核心优化策略

### 1. VLM 输入优化（收益最大）

```
方案 A：全图降分辨率
  1080p → 720p → VLM
  减少 token ~40%，精度损失小

方案 B：OCR 预提取 + 裁剪（推荐）
  OCR 识别文字密集区域 → 裁剪关键区域 → 720p → VLM
  减少 token 50-70%，精度几乎无损

方案 C：多级策略（最佳）
  1. 先 OCR 提取全部文字
  2. 识别区域类型（代码区/错误区/普通文本区）
  3. 有错误/异常 → 裁剪错误区域送 VLM
  4. 无异常 → 低分辨率全图送 VLM
```

### 2. 并行化

```
串行：截图 → OCR → 图片预处理 → VLM → 展示
                  总时间 = 各项之和

并行：截图 → OCR ──┬→ 图片预处理 (并行等待 OCR 结果) → VLM → 展示
                   └→ 提前编码图片并缓存
                  总时间 ≈ max(OCR, 预处理) + VLM + 展示
```

### 3. OCR 加速

```
- PP-OCRv3 模型 → PP-OCRv4 轻量版（如果可用，速度更快）
- INT8 量化：模型体积和推理时间各减少 ~60%
- TensorRT：NVIDIA GPU 额外 1.5-2x 加速
- 输入分辨率上限 960px（再高收益递减）
```

### 4. 连接池与预热

```csharp
// C# 侧：HttpClient 单例 + 连接池
private static readonly HttpClient _client = new()
{
    BaseAddress = new Uri("http://localhost"),
    Timeout = TimeSpan.FromSeconds(10),
    DefaultRequestVersion = HttpVersion.Version20,  // 多路复用
};

// Python 侧：模型启动时加载，保持常驻
@app.on_event("startup")
async def load_models():
    app.state.ocr = PaddleOCR(lang='ch', use_gpu=True)
```

### 5. 降级策略

| 场景 | 策略 |
|---|---|
| GPU OOM | VLM 降级为 CPU 推理 (llama.cpp)，牺牲速度保可用 |
| OCR 超时 | 跳过 OCR，直接送全图给 VLM（降精度保体验） |
| ChromaDB 写入失败 | 内存缓存，后台重试，不影响主流程 |

---

## 历史追问链路

```
用户输入文本 → Embedding 向量化 (50ms) → ChromaDB 检索 (30ms)
                                              │
                                              ▼
                                    返回相关上下文 + 新问题
                                              │
                                              ▼
                                    VLM 推理 (跳过，复用缓存) → 展示
                                    或 轻量回复（不调 VLM）
```

| 环节 | 典型耗时 |
|---|---|
| Embedding 生成 | ~30ms |
| ChromaDB 检索 | ~20ms |
| 上下文拼接 | ~5ms |
| 结果渲染 | ~30ms |
| **总计** | **< 100ms** |

> 历史追问不调用 VLM，直接从 ChromaDB 返回缓存的上下文回答，因此可以做到 < 1 秒。当缓存未命中时才降级为 VLM 推理。

---

## 延迟监控指标

```csharp
// 在 C# 客户端埋点
using var activity = DiagnosticListener.StartActivity("AskShot.analyze");

// 记录各阶段耗时
logger.LogInformation("Capture: {Elapsed}ms", captureElapsed);
logger.LogInformation("OCR: {Elapsed}ms", ocrElapsed);
logger.LogInformation("VLM: {Elapsed}ms", vlmElapsed);
logger.LogInformation("Total: {Elapsed}ms", totalElapsed);
```

MVP 阶段用简单的 Stopwatch 记录即可，后续可接入 Jaeger/Zipkin 做分布式追踪。
