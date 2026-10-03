"""Read-only tool implementations.

Every tool reaches the platform through the API gateway's read-only routes. The
agent platform has no database connection, no exchange client and no credential
of any kind — its network access is one HTTP client pointed at the gateway.
"""

from __future__ import annotations

from typing import Any

import httpx
import structlog

from agentiva_agents.config import Settings
from agentiva_agents.tools.registry import ToolRegistry

logger = structlog.get_logger(__name__)


class PlatformReadClient:
    """HTTP client for the gateway's read-only routes.

    Only ``GET`` is exposed. There is no ``post``, ``put`` or ``delete`` method
    on this class, so no tool built on it can mutate platform state even by
    mistake — the capability is absent rather than merely unused.
    """

    def __init__(self, settings: Settings, client: httpx.AsyncClient | None = None) -> None:
        self._settings = settings
        self._client = client or httpx.AsyncClient(
            base_url=settings.gateway_base_url,
            timeout=httpx.Timeout(settings.gateway_timeout_seconds),
            headers={"Accept": "application/json"},
        )

    async def get(self, path: str, params: dict[str, Any] | None = None) -> Any:
        """Performs a read against the gateway.

        Returns an empty payload rather than raising when the upstream is
        unavailable. An agent must be able to reason about missing evidence —
        and say so in its confidence — rather than having the whole run abort
        because one optional data source was down.
        """
        try:
            response = await self._client.get(path, params=params)
            response.raise_for_status()
            return response.json()
        except httpx.HTTPStatusError as exc:
            logger.warning(
                "tool_read_failed",
                path=path,
                status=exc.response.status_code,
            )
            return {}
        except httpx.HTTPError as exc:
            logger.warning("tool_read_unreachable", path=path, error=str(exc))
            return {}

    async def aclose(self) -> None:
        """Closes the underlying HTTP client."""
        await self._client.aclose()


def register_market_tools(registry: ToolRegistry, client: PlatformReadClient) -> None:
    """Registers every read-only tool available to agents."""

    async def get_market_data(symbol: str) -> Any:
        return await client.get(f"/market/symbols/{symbol}/ticker")

    async def get_candles(symbol: str, timeframe: str = "15m", limit: int = 200) -> Any:
        return await client.get(
            f"/market/symbols/{symbol}/candles",
            params={"timeframe": timeframe, "limit": min(limit, 1000)},
        )

    async def get_indicators(symbol: str, timeframe: str = "15m") -> Any:
        return await client.get(
            f"/market/symbols/{symbol}/indicators", params={"timeframe": timeframe}
        )

    async def get_orderbook(symbol: str, depth: int = 20) -> Any:
        return await client.get(
            f"/market/symbols/{symbol}/orderbook", params={"depth": min(depth, 100)}
        )

    async def get_portfolio() -> Any:
        return await client.get("/portfolio/summary")

    async def get_positions() -> Any:
        return await client.get("/portfolio/positions")

    async def get_strategy_performance(strategy_id: str | None = None) -> Any:
        path = (
            f"/strategies/{strategy_id}/performance"
            if strategy_id
            else "/strategies/performance"
        )
        return await client.get(path)

    async def get_recent_signals(symbol: str | None = None, limit: int = 20) -> Any:
        return await client.get(
            "/strategies/signals",
            params={"symbol": symbol, "limit": min(limit, 100)} if symbol else {"limit": limit},
        )

    async def get_risk_policy() -> Any:
        """Reads the active risk policy.

        Read-only and useful: an agent that knows the minimum confidence
        threshold can decline to propose a trade it knows would be rejected,
        which saves a pointless round trip. It cannot change the policy.
        """
        return await client.get("/risk/policies/default")

    async def get_news(symbol: str | None = None, limit: int = 20) -> Any:
        """Returns approved market news.

        Phase 7 wires an approved news source. Until then this returns an empty
        set, and the sentiment agent reports low confidence accordingly rather
        than inventing a sentiment reading.
        """
        _ = (symbol, limit)
        return {"headlines": [], "source": "not_configured"}

    registry.register(
        "get_market_data",
        "Returns the latest ticker for a symbol: bid, ask, last price and exchange timestamp.",
        get_market_data,
        {"symbol": {"type": "string", "description": "Trading pair, e.g. BTCUSDT", "required": True}},
    )
    registry.register(
        "get_candles",
        "Returns recent closed OHLCV candles for a symbol and timeframe.",
        get_candles,
        {
            "symbol": {"type": "string", "description": "Trading pair", "required": True},
            "timeframe": {"type": "string", "description": "Interval, e.g. 15m or 1h"},
            "limit": {"type": "integer", "description": "Candles to return, maximum 1000"},
        },
    )
    registry.register(
        "get_indicators",
        "Returns computed indicator values (EMA, RSI, MACD, ATR, VWAP) for a symbol.",
        get_indicators,
        {
            "symbol": {"type": "string", "description": "Trading pair", "required": True},
            "timeframe": {"type": "string", "description": "Interval, e.g. 15m or 1h"},
        },
    )
    registry.register(
        "get_orderbook",
        "Returns the current order book depth for a symbol.",
        get_orderbook,
        {
            "symbol": {"type": "string", "description": "Trading pair", "required": True},
            "depth": {"type": "integer", "description": "Levels per side, maximum 100"},
        },
    )
    registry.register(
        "get_portfolio",
        "Returns portfolio value, available balance, exposure and P&L.",
        get_portfolio,
    )
    registry.register(
        "get_positions",
        "Returns all open positions with size, average entry and unrealised P&L.",
        get_positions,
    )
    registry.register(
        "get_strategy_performance",
        "Returns historical performance metrics for a strategy, or for all strategies.",
        get_strategy_performance,
        {"strategy_id": {"type": "string", "description": "Strategy identifier"}},
    )
    registry.register(
        "get_recent_signals",
        "Returns recent deterministic strategy signals.",
        get_recent_signals,
        {
            "symbol": {"type": "string", "description": "Trading pair"},
            "limit": {"type": "integer", "description": "Signals to return, maximum 100"},
        },
    )
    registry.register(
        "get_risk_policy",
        "Returns the active risk policy limits. Read-only; agents cannot change them.",
        get_risk_policy,
    )
    registry.register(
        "get_news",
        "Returns approved market news headlines for a symbol.",
        get_news,
        {
            "symbol": {"type": "string", "description": "Trading pair"},
            "limit": {"type": "integer", "description": "Headlines to return"},
        },
    )
