"""Tests for the get_onchain_metrics tool.

No network call is made: httpx.MockTransport substitutes for the wire.
"""

from __future__ import annotations

import httpx

from agentiva_agents.config import Settings
from agentiva_agents.tools.onchain_tools import OnChainClient, register_onchain_tools
from agentiva_agents.tools.registry import ToolRegistry


def _client_with(handler) -> OnChainClient:
    settings = Settings()
    transport = httpx.MockTransport(handler)
    inner = httpx.AsyncClient(base_url=settings.onchain_base_url, transport=transport)
    return OnChainClient(settings, client=inner)


class TestOnChainClient:
    async def test_a_non_bitcoin_symbol_is_unavailable_without_a_network_call(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            raise AssertionError("No request should be made for a non-BTC symbol.")

        client = _client_with(handler)
        try:
            result = await client.get_metrics("ETHUSDT")
            assert result == {"available": False}
        finally:
            await client.aclose()

    async def test_a_bitcoin_symbol_returns_real_fields(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(
                200,
                json={
                    "hash_rate": 500000000.0,
                    "difficulty": 70000000000000.0,
                    "n_tx": 3000,
                    "n_tx_total": 900000000,
                    "miners_revenue_usd": 40000000.0,
                    "market_price_usd": 65000.0,
                },
            )

        client = _client_with(handler)
        try:
            result = await client.get_metrics("BTCUSDT")
            assert result["available"] is True
            assert result["asset"] == "BTC"
            assert result["hash_rate_gh_s"] == 500000000.0
            assert result["difficulty"] == 70000000000000.0
        finally:
            await client.aclose()

    async def test_an_http_error_degrades_to_unavailable(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(503)

        client = _client_with(handler)
        try:
            result = await client.get_metrics("BTCUSDT")
            assert result == {"available": False}
        finally:
            await client.aclose()

    async def test_an_unreachable_host_degrades_to_unavailable(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            raise httpx.ConnectError("connection refused")

        client = _client_with(handler)
        try:
            result = await client.get_metrics("BTCUSDT")
            assert result == {"available": False}
        finally:
            await client.aclose()


class TestGetOnChainMetricsToolRegistration:
    async def test_the_tool_is_permitted_and_callable(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, json={"hash_rate": 1.0})

        client = _client_with(handler)
        try:
            registry = ToolRegistry()
            register_onchain_tools(registry, client)
            registry.assert_complete()

            tool = registry.get("get_onchain_metrics")
            result = await tool.handler(symbol="BTCUSDT")
            assert result["available"] is True
        finally:
            await client.aclose()
