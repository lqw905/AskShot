"""Pydantic models for AskShot inference service."""

from pydantic import BaseModel


class ApiConfig(BaseModel):
    endpoint: str = "http://localhost:8080/v1"
    api_key: str = ""
    model: str = "qwen2.5-vl-3b-instruct"
    temperature: float = 0.7
    max_tokens: int = 2048


class AnalyzeRequest(BaseModel):
    image_base64: str
    user_question: str | None = None
    previous_answer: str | None = None
    api_config: ApiConfig | None = None


class ApiTestRequest(BaseModel):
    api_config: ApiConfig


class ApiTestResponse(BaseModel):
    ok: bool
    message: str


class AnalyzeResponse(BaseModel):
    summary: str


class HealthResponse(BaseModel):
    status: str


class HistoryItem(BaseModel):
    analysis: str = ""
    user_question: str = ""
    screenshot_path: str | None = None
    image_hash: str = ""
    tags: list[str] = []


class SearchRequest(BaseModel):
    query: str
    limit: int = 10
