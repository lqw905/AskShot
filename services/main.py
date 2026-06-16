"""ScreenMind inference service — single FastAPI process (:8900)

VLM proxy (screenshot → LLM directly, no OCR) + file-based history.
No database, no Docker, no OCR engine. Just Python.
"""

from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, HTTPException
import uvicorn

from models import (
    AnalyzeRequest, AnalyzeResponse,
    HistoryItem, SearchRequest,
    HealthResponse, ApiConfig, ApiTestRequest, ApiTestResponse,
)
from vlm_proxy import VlmProxy
from history import HistoryStore

vlm_proxy: VlmProxy | None = None
history_store: HistoryStore | None = None


@asynccontextmanager
async def lifespan(app: FastAPI):
    global vlm_proxy, history_store

    print("[ScreenMind] Starting...")
    vlm_proxy = VlmProxy()
    history_store = HistoryStore(data_dir=Path("data"))
    print("[ScreenMind] Ready on :8900")

    yield

    print("[ScreenMind] Shutting down...")


app = FastAPI(title="ScreenMind", lifespan=lifespan)


@app.get("/health", response_model=HealthResponse)
async def health():
    return HealthResponse(status="ok")


@app.post("/analyze", response_model=AnalyzeResponse)
async def analyze(request: AnalyzeRequest):
    """Send screenshot to VLM, return analysis."""
    if not vlm_proxy:
        raise HTTPException(503, "VLM proxy not available")
    try:
        return await vlm_proxy.analyze(
            image_base64=request.image_base64,
            user_question=request.user_question,
            api_config=request.api_config or ApiConfig(),
        )
    except Exception as ex:
        raise HTTPException(502, str(ex)) from ex


@app.post("/config/test", response_model=ApiTestResponse)
async def test_config(request: ApiTestRequest):
    """Check the configured OpenAI-compatible API endpoint."""
    if not vlm_proxy:
        raise HTTPException(503, "VLM proxy not available")
    ok, message = await vlm_proxy.test_connection(request.api_config)
    return ApiTestResponse(ok=ok, message=message)


@app.post("/history/save")
async def save_history(item: HistoryItem):
    """Save analysis result to JSON file."""
    if not history_store:
        raise HTTPException(503, "History store not available")
    record_id = history_store.save(
        ocr_text="",  # no OCR text — VLM sees the image directly
        analysis=item.analysis,
        user_question=item.user_question,
        screenshot_path=item.screenshot_path,
        image_hash=item.image_hash,
        tags=item.tags,
    )
    return {"id": record_id, "status": "ok"}


@app.get("/history/recent")
async def get_recent(limit: int = 10, hours: int = 24):
    if not history_store:
        raise HTTPException(503)
    return {"results": history_store.get_recent(limit=limit, hours=hours)}


@app.post("/history/search")
async def search_history(request: SearchRequest):
    if not history_store:
        raise HTTPException(503)
    return {"results": history_store.search(request.query, request.limit)}


@app.post("/history/favorite/{record_id}")
async def toggle_favorite(record_id: str):
    if not history_store:
        raise HTTPException(503)
    is_fav = history_store.toggle_favorite(record_id)
    return {"id": record_id, "is_favorite": is_fav}


@app.get("/history/favorites")
async def get_favorites():
    if not history_store:
        raise HTTPException(503)
    return {"results": history_store.get_favorites()}


if __name__ == "__main__":
    # Fix models import for direct execution
    import sys
    sys.path.insert(0, str(Path(__file__).parent))
    import models  # noqa: F811
    uvicorn.run(app, host="127.0.0.1", port=8900)
