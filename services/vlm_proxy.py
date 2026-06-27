"""VLM proxy - sends screenshot directly to user-configured OpenAI-compatible API."""

import re

import httpx

from models import ApiConfig, AnalyzeResponse

SYSTEM_PROMPT = """你是一个截图文字解释器。用户通常框选的是一小段不理解的文字，目的是立刻知道它是什么意思并理解相关知识。

回答策略：
1. 先判断截图里有没有可读文字。有文字时，只围绕文字解释，不要描述界面、窗口、颜色、按钮位置等无关信息。
2. 如果文字清晰，直接解释它的含义就好。
3. 再补充必要背景：它出现在什么语境、为什么会这样、用户应该怎么理解。
4. 如果是错误、警告、配置项、按钮、代码、英文术语或文档句子，重点解释含义和下一步该怎么做。
5. 如果截图里没有明显可读文字，才简短描述图片内容。
6. 不要臆造看不清的文字；看不清时明确说"这部分看不清"。

以下短语严禁出现在回复中（一个都不要用）：
- ❌ 这句话的意思是、该词表示、即：、也就是说
- ❌ 总结：、小结：、总之、综上、总的来说
- ❌ 记住、需要注意的是、值得注意的是
- ❌ 截图展示了、如图所示、上图显示

输出要求：
- 使用中文，简洁准确，3 到 6 行。
- 不要使用 Markdown 格式（**、###、* 等符号）。
- 不要用"记住"来引导记忆口诀或任何内容。"""

FOLLOWUP_SYSTEM_PROMPT = """你是一个截图问答助手。用户针对刚才看过的截图继续提问，请用简洁、准确的中文直接回答他的问题。不要在开头重复或复述用户的问题，直接给出答案。不要重复解释整张截图，聚焦问题本身。不要使用 Markdown。"""

DEFAULT_FIRST_QUESTION = "请只解释截图中文字的含义，帮助我理解；如果没有文字，再简短描述图片内容。不要使用 Markdown 符号。"

MOCK_RESPONSE = """1. 内容识别：这是一个桌面应用截图，包含界面元素和文字信息。

2. 关键信息：检测到窗口标题栏、文本内容、按钮等常见 UI 元素。

3. 状态分析：未检测到明显的错误或异常信息。

4. 建议：如需使用真实 AI 分析，请在控制台配置视觉模型 API。
"""


class VlmProxy:
    async def test_connection(self, api_config: ApiConfig) -> tuple[bool, str]:
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

    async def analyze(
        self,
        image_base64: str,
        user_question: str | None,
        previous_answer: str | None,
        api_config: ApiConfig,
    ) -> AnalyzeResponse:
        if not api_config.endpoint or api_config.endpoint == "mock":
            return AnalyzeResponse(summary=MOCK_RESPONSE)

        image_item = {
            "type": "image_url",
            "image_url": {"url": f"data:image/png;base64,{image_base64}"},
        }

        is_followup = bool(previous_answer)
        sys_prompt = FOLLOWUP_SYSTEM_PROMPT if is_followup else SYSTEM_PROMPT

        if is_followup:
            first_text = DEFAULT_FIRST_QUESTION
            messages = [
                {"role": "system", "content": sys_prompt},
                {
                    "role": "user",
                    "content": [image_item, {"type": "text", "text": first_text}],
                },
                {"role": "assistant", "content": previous_answer or ""},
                {
                    "role": "user",
                    "content": [
                        image_item,
                        {"type": "text", "text": user_question or "继续提问"},
                    ],
                },
            ]
        else:
            first_text = user_question or DEFAULT_FIRST_QUESTION
            messages = [
                {"role": "system", "content": sys_prompt},
                {
                    "role": "user",
                    "content": [image_item, {"type": "text", "text": first_text}],
                },
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

            summary = clean_response_text(data["choices"][0]["message"]["content"])
            return AnalyzeResponse(summary=summary)
        except httpx.HTTPStatusError as ex:
            raise Exception(f"API Error {ex.response.status_code}: {ex.response.text[:300]}")
        except Exception as ex:
            import traceback
            raise Exception(f"VLM request failed: {ex}\n{traceback.format_exc()}")


def clean_response_text(text: str) -> str:
    if not text:
        return ""

    cleaned = text.replace("\r\n", "\n").replace("\r", "\n")
    cleaned = re.sub(r"```[a-zA-Z0-9_-]*\n?", "", cleaned)
    cleaned = cleaned.replace("```", "")
    cleaned = re.sub(r"(?m)^\s{0,3}#{1,6}\s*", "", cleaned)
    cleaned = re.sub(r"\*\*([^*\n]+)\*\*", r"\1", cleaned)
    cleaned = re.sub(r"__([^_\n]+)__", r"\1", cleaned)
    cleaned = re.sub(r"(?<!\*)\*([^*\n]+)\*(?!\*)", r"\1", cleaned)
    cleaned = re.sub(r"(?m)^\s*[*+-]\s+", "• ", cleaned)
    cleaned = re.sub(r"(?m)^\s*[-*_]{3,}\s*$", "", cleaned)
    cleaned = re.sub(r"[ \t]+\n", "\n", cleaned)
    cleaned = re.sub(r"\n{3,}", "\n\n", cleaned)

    # 去除模型回复中常见但生硬的模板化表达
    cleaned = re.sub(
        r"(这句话的意思是|该词表示|即：|也就是说，|"
        r"^总结[：:].*(\n|$)|^小结[：:].*(\n|$)|^总之[，,].*(\n|$)|^综上[，,].*(\n|$)|"
        r"记住\S*[：:]|需要注意的是|值得注意的?是)",
        "", cleaned, flags=re.MULTILINE)
    cleaned = re.sub(r"\n{3,}", "\n\n", cleaned)  # 再次合并空行

    return cleaned.strip()
