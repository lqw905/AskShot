# 知识检索与记忆系统设计

## 设计原则

**零数据库、零依赖**。用文件系统做存储，纯文本搜索做检索。MVP 阶段每天几十条记录，JSON 文件完全够用。

---

## 存储结构

```
data/
├── history/
│   ├── 2026-06-16_143052_abc12345.json    # 每次分析一个 JSON
│   ├── 2026-06-16_150830_def67890.json
│   └── ...
├── screenshots/                            # 可选：截图原图
│   ├── 2026-06-16_143052_abc12345.png
│   └── 2026-06-16_150830_def67890.png
└── favorites.json                          # 收藏列表（id 数组）
```

### 单条记录格式

```json
{
  "id": "2026-06-16_143052_abc12345",
  "timestamp": "2026-06-16T14:30:52Z",
  "ocr_text": "OCR 提取的完整文字...",
  "analysis": "VLM 分析的完整结果...",
  "user_question": "这是什么报错？",
  "screenshot_path": "screenshots/2026-06-16_143052_abc12345.png",
  "tags": [],
  "is_favorite": false
}
```

### 数据量预估

- 单条 JSON：~3-10KB（分析文本为主）
- 单张截图 PNG：~200KB-1MB
- 日均 30 条：文本 ~300KB + 截图 ~15MB（可选）
- 年累计：文本 ~100MB + 截图 ~5GB（可选）
- **无数据库引擎开销，文件系统本身就能处理这个量级**

---

## 检索方式（全文本搜索，无向量）

### 1. 时间窗口（"刚才那个"）

直接按文件名中的时间戳排序，取最近 N 条：

```python
def retrieve_recent(limit: int = 10, hours: int = 24) -> list[dict]:
    cutoff = datetime.utcnow() - timedelta(hours=hours)
    results = []
    for f in sorted(Path("data/history").glob("*.json"), reverse=True):
        mtime = datetime.fromtimestamp(f.stat().st_mtime, tz=timezone.utc)
        if mtime < cutoff:
            continue
        results.append(json.loads(f.read_text(encoding="utf-8")))
        if len(results) >= limit:
            break
    return results
```

### 2. 关键词搜索（"类似的还有"）

用 OCR 文本 + 分析文本做全文匹配，按匹配度排序：

```python
def search_keyword(query: str, limit: int = 10) -> list[dict]:
    keywords = query.lower().split()
    scored = []
    for f in Path("data/history").glob("*.json"):
        data = json.loads(f.read_text(encoding="utf-8"))
        text = (data.get("ocr_text", "") + " " + data.get("analysis", "")).lower()
        score = sum(text.count(kw) for kw in keywords)
        if score > 0:
            scored.append((score, data))
    scored.sort(key=lambda x: x[0], reverse=True)
    return [item for _, item in scored[:limit]]
```

### 3. 收藏列表

```python
def get_favorites() -> list[dict]:
    favs = json.loads(Path("data/favorites.json").read_text())
    return [load_history_item(fid) for fid in favs]

def toggle_favorite(id: str) -> bool:
    favs = json.loads(Path("data/favorites.json").read_text())
    if id in favs:
        favs.remove(id)
    else:
        favs.append(id)
    Path("data/favorites.json").write_text(json.dumps(favs, indent=2))
    return id in favs
```

---

## Python 端代码（集成到主服务）

```python
# 集成在 services/main.py 中，无需单独服务
import json
import os
from pathlib import Path
from datetime import datetime, timedelta, timezone
from fastapi import FastAPI
from pydantic import BaseModel

HISTORY_DIR = Path("data/history")
SCREENSHOT_DIR = Path("data/screenshots")
FAVORITES_FILE = Path("data/favorites.json")

# 确保目录存在
HISTORY_DIR.mkdir(parents=True, exist_ok=True)
SCREENSHOT_DIR.mkdir(parents=True, exist_ok=True)
if not FAVORITES_FILE.exists():
    FAVORITES_FILE.write_text("[]")


@app.post("/history/save")
async def save_history(item: HistoryItem):
    """保存分析结果"""
    ts = datetime.utcnow()
    item_id = f"{ts.strftime('%Y-%m-%d_%H%M%S')}_{item.image_hash[:8]}"
    record = {
        "id": item_id,
        "timestamp": ts.isoformat(),
        "ocr_text": item.ocr_text,
        "analysis": item.analysis,
        "user_question": item.user_question,
        "screenshot_path": item.screenshot_path,
        "tags": item.tags or [],
        "is_favorite": False,
    }
    (HISTORY_DIR / f"{item_id}.json").write_text(
        json.dumps(record, ensure_ascii=False, indent=2),
        encoding="utf-8"
    )
    return {"id": item_id, "status": "ok"}


@app.get("/history/recent")
async def get_recent(limit: int = 10, hours: int = 24):
    """获取最近 N 小时内的记录"""
    cutoff = datetime.utcnow() - timedelta(hours=hours)
    results = []
    files = sorted(HISTORY_DIR.glob("*.json"), key=lambda f: f.stat().st_mtime, reverse=True)
    for f in files:
        if len(results) >= limit:
            break
        mtime = datetime.fromtimestamp(f.stat().st_mtime, tz=timezone.utc)
        if mtime < cutoff:
            continue
        results.append(json.loads(f.read_text(encoding="utf-8")))
    return {"results": results}


@app.post("/history/search")
async def search_history(request: SearchRequest):
    """关键词搜索历史"""
    keywords = request.query.lower().split()
    scored = []
    for f in HISTORY_DIR.glob("*.json"):
        data = json.loads(f.read_text(encoding="utf-8"))
        text = f"{data.get('ocr_text', '')} {data.get('analysis', '')}".lower()
        score = sum(text.count(kw) for kw in keywords)
        if score > 0:
            scored.append((score, data))
    scored.sort(key=lambda x: x[0], reverse=True)
    return {"results": [item for _, item in scored[:request.limit]]}


@app.post("/history/favorite/{item_id}")
async def toggle_favorite(item_id: str):
    """切换收藏状态"""
    favs = json.loads(FAVORITES_FILE.read_text())
    if item_id in favs:
        favs.remove(item_id)
    else:
        favs.append(item_id)
    FAVORITES_FILE.write_text(json.dumps(favs, indent=2))
    # 同步更新原始记录
    record_file = HISTORY_DIR / f"{item_id}.json"
    if record_file.exists():
        record = json.loads(record_file.read_text(encoding="utf-8"))
        record["is_favorite"] = item_id in favs
        record_file.write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")
    return {"is_favorite": item_id in favs}


@app.get("/history/favorites")
async def get_favorites():
    """获取收藏列表"""
    favs = json.loads(FAVORITES_FILE.read_text())
    results = []
    for fid in favs:
        f = HISTORY_DIR / f"{fid}.json"
        if f.exists():
            results.append(json.loads(f.read_text(encoding="utf-8")))
    return {"results": results}
```

---

## 截图保存控制

由 C# 客户端根据控制台配置决定是否保存截图：

```csharp
// 控制台配置
public class DataConfig
{
    public bool SaveScreenshots { get; set; } = false;       // 默认不保存（省空间）
    public string ScreenshotPath { get; set; } = "";          // 空 = 使用默认路径
    public bool SaveHistory { get; set; } = true;             // 默认保存分析记录
    public string HistoryPath { get; set; } = "";             // 空 = 使用默认路径
    public int HistoryRetentionDays { get; set; } = 30;
}

// 截图处理
public async Task<AnalysisResult> ProcessScreenshot(Bitmap screenshot)
{
    var base64 = ToBase64(screenshot);

    // 1. OCR + VLM 分析（不变）
    var ocrResult = await _client.PostAsJsonAsync("/ocr", ...);
    var analyzeResult = await _client.PostAsJsonAsync("/analyze", ...);

    // 2. 根据配置决定是否保存截图
    string? screenshotPath = null;
    if (_config.Data.SaveScreenshots)
    {
        var dir = string.IsNullOrEmpty(_config.Data.ScreenshotPath)
            ? Path.Combine(AppContext.BaseDirectory, "data", "screenshots")
            : _config.Data.ScreenshotPath;
        Directory.CreateDirectory(dir);
        var fileName = $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{HashImage(screenshot)}.png";
        screenshotPath = Path.Combine(dir, fileName);
        screenshot.Save(screenshotPath, ImageFormat.Png);
    }

    // 3. 保存分析记录
    if (_config.Data.SaveHistory)
    {
        await _client.PostAsJsonAsync("/history/save", new
        {
            ocr_text = ocrResult.Text,
            analysis = analyzeResult.Summary,
            user_question = "",
            screenshot_path = screenshotPath,
            image_hash = HashImage(screenshot),
        });
    }

    return analyzeResult;
}
```

---

## 定期清理

```python
# 在 FastAPI lifespan 中注册后台清理任务
async def cleanup_old_records(retention_days: int = 30):
    cutoff = datetime.utcnow() - timedelta(days=retention_days)
    for f in HISTORY_DIR.glob("*.json"):
        mtime = datetime.fromtimestamp(f.stat().st_mtime, tz=timezone.utc)
        if mtime < cutoff:
            data = json.loads(f.read_text(encoding="utf-8"))
            # 同时删除关联截图
            if data.get("screenshot_path"):
                ss = Path(data["screenshot_path"])
                if ss.exists():
                    ss.unlink()
            f.unlink()
```

---

## 后续演进方向

| 阶段 | 能力 | 方案 |
|---|---|---|
| MVP | 时间窗口 + 关键词搜索 | JSON 文件 + 纯文本匹配 |
| V2 | 语义搜索 | 加入 SentenceTransformers，向量存为 `.npy` 文件 |
| V3 | 自动标签 | 文本分类模型 |
| V4 | 以图搜图 | CLIP embedding |

> MVP 不做语义搜索。数据量小（日均几十条）时，关键词匹配已经够用。等用户积累了上千条记录再引入向量检索也不迟。
