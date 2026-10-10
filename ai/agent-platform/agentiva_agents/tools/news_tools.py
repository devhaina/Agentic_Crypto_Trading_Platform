"""The approved news tool, backed by CryptoCompare's news API.

Like :class:`~agentiva_agents.tools.market_tools.PlatformReadClient`, this
client only exposes ``GET``. It reaches a third-party public API rather than
the platform's own gateway, but the same rule applies: no credential it holds
can place or modify a trade, and no failure here aborts an agent run — the
sentiment agent treats an empty, unavailable result as evidence to report low
confidence on, not as a reason to crash.
"""

from __future__ import annotations

from typing import Any

import httpx
import structlog

from agentiva_agents.config import Settings
from agentiva_agents.tools.registry import ToolRegistry
from agentiva_agents.tools.symbols import base_asset_of

logger = structlog.get_logger(__name__)


class NewsClient:
    """Read-only client for CryptoCompare's news endpoint.

    Configured with no key, every call reports ``not_configured`` without
    attempting a request — an unauthenticated call to this API reliably 401s,
    so there is nothing to learn by making it. With a key, any failure
    (401, timeout, malformed body) degrades to an ``unavailable`` result
    rather than raising, matching the fail-safe convention used across every
    other tool in this platform.
    """

    def __init__(self, settings: Settings, client: httpx.AsyncClient | None = None) -> None:
        self._api_key = settings.news_api_key
        self._client = client or httpx.AsyncClient(
            base_url=settings.news_base_url,
            timeout=httpx.Timeout(settings.gateway_timeout_seconds),
            headers={"Accept": "application/json"},
        )

    async def get_headlines(self, symbol: str | None, limit: int) -> dict[str, Any]:
        if not self._api_key:
            return {"headlines": [], "source": "not_configured"}

        categories = base_asset_of(symbol) if symbol else None
        try:
            response = await self._client.get(
                "/data/v2/news/",
                params={
                    "lang": "EN",
                    "api_key": self._api_key,
                    **({"categories": categories} if categories else {}),
                },
            )
            response.raise_for_status()
            body = response.json()
        except httpx.HTTPStatusError as exc:
            logger.warning("news_read_failed", status=exc.response.status_code)
            return {"headlines": [], "source": "unavailable"}
        except httpx.HTTPError as exc:
            logger.warning("news_read_unreachable", error=str(exc))
            return {"headlines": [], "source": "unavailable"}

        articles = body.get("Data") if isinstance(body, dict) else None
        if not isinstance(articles, list):
            return {"headlines": [], "source": "unavailable"}

        headlines = [
            {
                "title": article.get("title", ""),
                "source": article.get("source_info", {}).get("name", "")
                if isinstance(article.get("source_info"), dict)
                else "",
                "published_at": article.get("published_on", ""),
                "url": article.get("url", ""),
            }
            for article in articles[:limit]
            if isinstance(article, dict)
        ]
        return {"headlines": headlines, "source": "cryptocompare"}

    async def aclose(self) -> None:
        await self._client.aclose()


def register_news_tools(registry: ToolRegistry, client: NewsClient) -> None:
    """Registers the real ``get_news`` tool."""

    async def get_news(symbol: str | None = None, limit: int = 20) -> Any:
        return await client.get_headlines(symbol, min(limit, 50))

    registry.register(
        "get_news",
        "Returns recent approved market news headlines, optionally filtered to a symbol's "
        "base asset. Reports 'not_configured' or 'unavailable' rather than fabricating "
        "headlines when no source can be reached.",
        get_news,
        {
            "symbol": {"type": "string", "description": "Trading pair, e.g. BTCUSDT"},
            "limit": {"type": "integer", "description": "Headlines to return, maximum 50"},
        },
    )
