"""VLM proxy — sends screenshot directly to user-configured OpenAI-compatible API."""

import httpx

from models import ApiConfig, AnalyzeResponse

SYSTEM_PROMPT = """你是一个截图分析助手。请分析这张截图，回答以下问题：
1. 这张截图展示了什么内容？（应用、页面、场景）
2. 关键文字信息是什么？
3. 如果有错误/异常，请解释原因和建议的解决方案。
4. 用户可能需要什么帮助？

请用中文简洁回答。"""

MOCK_RESPONSE = """📸 截图分析结果 (Mock 模式)

1. 内容识别：这是一个桌面应用截图，包含界面元素和文字信息。

2. 关键信息：检测到窗口标题栏、文本内容、按钮等常见 UI 元素。

3. 状态分析：未检测到明显的错误或异常信息。

4. 建议：如需使用真实 AI 分析，请在控制台配置视觉模型 API：
   - 本地模型：Ollama + minicpm-v / llama3.2-vision
   - 云端 API：OpenAI GPT-4V / 其他兼容接口

💡 在控制台 → LLM 配置中填入 API 地址即可切换为真实分析。"""


class VlmProxy:
    async def test_connection(self, api_config: ApiConfig) -> tuple[bool, str]:
        """Check the configured OpenAI-compatible endpoint without running inference."""
        endpoint = (api_config.endpoint or "").strip().rstrip("/")
        if not endpoint or endpoint == "mock":
            return False, "未配置 API 地址，当前会进入 Mock 模式。"

        try:
            async with httpx.AsyncClient(timeout=10) as client:
                headers = {}
                if api_config.api_key:
                    headers["Authorization"] = f"Bearer {api_config.api_key}"

                resp = await client.get(f"{endpoint}/models", headers=headers)
                if resp.status_code == 200:
                    return True, "LLM API 连接正常。"
                if resp.status_code in (401, 403):
                    return False, "API 地址可访问，但 API Key 未授权或权限不足。"
                if resp.status_code == 404:
                    return False, "API 地址可访问，但 /models 不存在；请确认地址是否应包含 /v1。"
                return False, f"API 返回 HTTP {resp.status_code}: {resp.text[:200]}"
        except httpx.ConnectError:
            return False, "无法连接到 API 地址，请检查服务是否启动、地址和端口是否正确。"
        except httpx.TimeoutException:
            return False, "连接 API 超时，请检查网络或本地模型服务状态。"
        except Exception as ex:
            return False, f"连接测试失败: {ex}"

    async def check_ready(self) -> bool:
        """Quick connectivity check to the default endpoint."""
        try:
            async with httpx.AsyncClient(timeout=3) as client:
                resp = await client.get("http://localhost:8080/v1/models")
                return resp.status_code == 200
        except Exception:
            return False

    async def analyze(
        self,
        image_base64: str,
        user_question: str | None,
        api_config: ApiConfig,
    ) -> AnalyzeResponse:
        """Send screenshot directly to VLM — mock mode if no endpoint configured."""
        # Mock mode: no API configured
        if not api_config.endpoint or api_config.endpoint == "mock":
            return AnalyzeResponse(summary=MOCK_RESPONSE)

        user_content = [
            {
                "type": "image_url",
                "image_url": {"url": f"data:image/png;base64,{image_base64}"},
            },
        ]

        text = user_question or "请分析这张截图的内容和含义。"
        user_content.append({"type": "text", "text": text})

        messages = [
            {"role": "system", "content": SYSTEM_PROMPT},
            {"role": "user", "content": user_content},
        ]

        headers = {"Content-Type": "application/json"}
        if api_config.api_key:
            headers["Authorization"] = f"Bearer {api_config.api_key}"

        try:
            async with httpx.AsyncClient(timeout=60) as client:
                resp = await client.post(
                    f"{api_config.endpoint.rstrip('/')}/chat/completions",
                    headers=headers,
                    json={
                        "model": api_config.model,
                        "messages": messages,
                        "temperature": api_config.temperature,
                        "max_tokens": api_config.max_tokens,
                    },
                )
                resp.raise_for_status()
                data = resp.json()

            summary = data["choices"][0]["message"]["content"]
            return AnalyzeResponse(summary=summary)
        except httpx.HTTPStatusError as ex:
            # Re-throw so the caller can see the HTTP error
            raise Exception(f"API Error {ex.response.status_code}: {ex.response.text[:300]}")
        except Exception as ex:
            import traceback
            raise Exception(f"VLM request failed: {ex}\n{traceback.format_exc()}")
