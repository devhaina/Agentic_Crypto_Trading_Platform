"""The on-chain metrics tool, backed by blockchain.info's public stats API.

Scoped to Bitcoin only: blockchain.info's keyless ``/stats`` endpoint is the
one on-chain data source this platform could verify as genuinely free and
reachable without a vendor account. Any other base asset honestly reports
``available: false`` rather than fabricating a number — matching
phases.md's own "on-chain metrics where available" qualifier.
"""

from __future__ import annotations

from typing import Any

import httpx
import structlog

from agentiva_agents.config import Settings
from agentiva_agents.tools.registry import ToolRegistry
from agentiva_agents.tools.symbols import base_asset_of

logger = structlog.get_logger(__name__)

_UNAVAILABLE: dict[str, Any] = {"available": False}


class OnChainClient:
    """Read-only client for blockchain.info's public Bitcoin network stats."""

    def __init__(self, settings: Settings, client: httpx.AsyncClient | None = None) -> None:
        self._client = client or httpx.AsyncClient(
            base_url=settings.onchain_base_url,
            timeout=httpx.Timeout(settings.gateway_timeout_seconds),
            headers={"Accept": "application/json"},
        )

    async def get_metrics(self, symbol: str) -> dict[str, Any]:
        if base_asset_of(symbol) != "BTC":
            return _UNAVAILABLE

        try:
            response = await self._client.get("/stats", params={"format": "json"})
            response.raise_for_status()
            body = response.json()
        except httpx.HTTPStatusError as exc:
            logger.warning("onchain_read_failed", status=exc.response.status_code)
            return _UNAVAILABLE
        except httpx.HTTPError as exc:
            logger.warning("onchain_read_unreachable", error=str(exc))
            return _UNAVAILABLE

        if not isinstance(body, dict):
            return _UNAVAILABLE

        return {
            "available": True,
            "asset": "BTC",
            "hash_rate_gh_s": body.get("hash_rate"),
            "difficulty": body.get("difficulty"),
            "mempool_transactions": body.get("n_tx"),
            "total_transactions_24h": body.get("n_tx_total"),
            "miners_revenue_usd": body.get("miners_revenue_usd"),
            "market_price_usd": body.get("market_price_usd"),
        }

    async def aclose(self) -> None:
        await self._client.aclose()


def register_onchain_tools(registry: ToolRegistry, client: OnChainClient) -> None:
    """Registers the ``get_onchain_metrics`` tool."""

    async def get_onchain_metrics(symbol: str) -> Any:
        return await client.get_metrics(symbol)

    registry.register(
        "get_onchain_metrics",
        "Returns on-chain network metrics (hash rate, difficulty, transaction volume) for "
        "a symbol's base asset. Only Bitcoin is covered today; any other asset reports "
        "available: false rather than a fabricated figure.",
        get_onchain_metrics,
        {
            "symbol": {
                "type": "string",
                "description": "Trading pair, e.g. BTCUSDT",
                "required": True,
            }
        },
    )
