"""Claude-backed provider.

Optional: the ``anthropic`` package is an extra, and the platform runs without
it on the deterministic stub provider. The factory degrades to the stub when the
package or the API key is absent rather than failing to start.
"""

from __future__ import annotations

import json
from typing import Any

import structlog

from agentiva_agents.llm.provider import CompletionRequest, CompletionResponse, LlmProvider

logger = structlog.get_logger(__name__)


class AnthropicLlmProvider(LlmProvider):
    """Produces structured output using the Claude Messages API."""

    def __init__(self, api_key: str, model: str) -> None:
        try:
            import anthropic
        except ImportError as exc:  # pragma: no cover - depends on an optional extra
            raise RuntimeError(
                "The anthropic package is not installed. Install the 'anthropic' extra, or set "
                "AI_LLM_PROVIDER=stub to use the deterministic provider."
            ) from exc

        self._client = anthropic.AsyncAnthropic(api_key=api_key)
        self._model = model

    @property
    def model_name(self) -> str:
        return self._model

    async def complete(self, request: CompletionRequest) -> CompletionResponse:
        """Requests a structured completion and parses the JSON result."""
        # The schema is restated in the prompt as well as being described by the
        # tool, because a model that is merely asked for JSON sometimes wraps it
        # in prose. The caller validates with Pydantic regardless.
        user_content = (
            f"{request.user_prompt}\n\n"
            f"## Context\n```json\n{json.dumps(request.context, indent=2, default=str)}\n```\n\n"
            f"## Required response schema\n"
            f"```json\n{json.dumps(request.response_schema, indent=2)}\n```\n\n"
            "Respond with a single JSON object matching the schema exactly. "
            "Do not include any text before or after the JSON."
        )

        # No `temperature` argument: the installed SDK's Messages API no longer
        # accepts one (removed upstream) — passing it raises TypeError before any
        # network call is made. request.temperature is kept on the contract for
        # providers that still support sampling control.
        message = await self._client.messages.create(
            model=self._model,
            max_tokens=request.max_tokens,
            system=request.system_prompt,
            messages=[{"role": "user", "content": user_content}],
        )

        from anthropic.types import TextBlock

        text = "".join(
            block.text for block in message.content if isinstance(block, TextBlock)
        ).strip()

        content = self._parse_json(text)

        return CompletionResponse(
            content=content,
            model=self._model,
            input_tokens=message.usage.input_tokens,
            output_tokens=message.usage.output_tokens,
            tools_used=list(request.context.get("tools_used", [])),
        )

    @staticmethod
    def _parse_json(text: str) -> dict[str, Any]:
        """Extracts a JSON object from a model response.

        Tolerates a fenced code block, which models commonly add. Anything
        genuinely unparseable raises: the caller treats a malformed response as a
        failed agent run, which is correct — far better than passing a
        half-understood object into the trading path.
        """
        candidate = text

        if candidate.startswith("```"):
            lines = [line for line in candidate.splitlines() if not line.startswith("```")]
            candidate = "\n".join(lines).strip()

        try:
            parsed = json.loads(candidate)
        except json.JSONDecodeError as exc:
            raise ValueError(
                f"The model did not return parseable JSON: {exc}. "
                f"Response began: {text[:200]!r}"
            ) from exc

        if not isinstance(parsed, dict):
            raise ValueError(
                f"Expected a JSON object but the model returned {type(parsed).__name__}."
            )

        return parsed


def create_provider(
    provider_name: str, api_key: str, model: str
) -> LlmProvider:
    """Builds the configured provider, falling back to the stub.

    A missing key or an uninstalled SDK degrades to deterministic analysis and
    logs loudly. The alternative — refusing to start — would take the service
    down over a configuration problem, and the stub is a perfectly safe default
    because no agent output can reach an exchange unreviewed anyway.
    """
    from agentiva_agents.llm.stub import StubLlmProvider

    if provider_name == "stub":
        return StubLlmProvider()

    if not api_key:
        logger.warning(
            "llm_provider_falling_back_to_stub",
            requested=provider_name,
            reason="no API key configured",
        )
        return StubLlmProvider()

    if provider_name == "anthropic":
        try:
            return AnthropicLlmProvider(api_key=api_key, model=model)
        except RuntimeError as exc:
            logger.warning(
                "llm_provider_falling_back_to_stub", requested=provider_name, reason=str(exc)
            )
            return StubLlmProvider()

    logger.warning(
        "llm_provider_falling_back_to_stub",
        requested=provider_name,
        reason="provider not implemented",
    )
    return StubLlmProvider()
