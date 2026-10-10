"""Tests for the real get_news tool.

No network call is made: httpx.MockTransport substitutes for the wire, so
these assert the client's own logic (auth gating, error handling, shape of
the result) rather than CryptoCompare's availability.
"""

from __future__ import annotations

import httpx

from agentiva_agents.config import Settings
from agentiva_agents.tools.news_tools import NewsClient, register_news_tools
from agentiva_agents.tools.registry import ToolRegistry


def _client_with(handler, api_key: str = "test-key") -> NewsClient:
    settings = Settings(news_api_key=api_key)
    transport = httpx.MockTransport(handler)
    inner = httpx.AsyncClient(base_url=settings.news_base_url, transport=transport)
    return NewsClient(settings, client=inner)


class TestNewsClient:
    async def test_no_api_key_reports_not_configured_without_a_network_call(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            raise AssertionError("No request should be made without a key.")

        client = _client_with(handler, api_key="")
        try:
            result = await client.get_headlines("BTCUSDT", 20)
            assert result == {"headlines": [], "source": "not_configured"}
        finally:
            await client.aclose()

    async def test_a_successful_response_is_normalised(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            assert request.url.params["api_key"] == "test-key"
            return httpx.Response(
                200,
                json={
                    "Data": [
                        {
                            "title": "Bitcoin surges to a new record high",
                            "source_info": {"name": "example-news"},
                            "published_on": 1700000000,
                            "url": "https://example.test/a",
                        }
                    ]
                },
            )

        client = _client_with(handler)
        try:
            result = await client.get_headlines("BTCUSDT", 20)
            assert result["source"] == "cryptocompare"
            assert len(result["headlines"]) == 1
            assert result["headlines"][0]["title"] == "Bitcoin surges to a new record high"
            assert result["headlines"][0]["source"] == "example-news"
        finally:
            await client.aclose()

    async def test_an_http_error_degrades_to_unavailable(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(401, json={"Message": "API key required"})

        client = _client_with(handler)
        try:
            result = await client.get_headlines("BTCUSDT", 20)
            assert result == {"headlines": [], "source": "unavailable"}
        finally:
            await client.aclose()

    async def test_an_unreachable_host_degrades_to_unavailable(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            raise httpx.ConnectError("connection refused")

        client = _client_with(handler)
        try:
            result = await client.get_headlines("BTCUSDT", 20)
            assert result == {"headlines": [], "source": "unavailable"}
        finally:
            await client.aclose()

    async def test_limit_is_respected(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(
                200,
                json={"Data": [{"title": f"Headline {i}"} for i in range(10)]},
            )

        client = _client_with(handler)
        try:
            result = await client.get_headlines("BTCUSDT", 3)
            assert len(result["headlines"]) == 3
        finally:
            await client.aclose()


class TestGetNewsToolRegistration:
    async def test_the_tool_is_permitted_and_callable(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, json={"Data": []})

        client = _client_with(handler)
        try:
            registry = ToolRegistry()
            register_news_tools(registry, client)
            registry.assert_complete()

            tool = registry.get("get_news")
            result = await tool.handler(symbol="BTCUSDT", limit=5)
            assert result == {"headlines": [], "source": "cryptocompare"}
        finally:
            await client.aclose()
