"""Tests for the deterministic stub provider's per-agent rules.

Focused on the Phase 7 additions: real keyword-based sentiment scoring (in
place of the always-zero placeholder) and the market agent noting on-chain
data when it is available.
"""

from __future__ import annotations

from decimal import Decimal

from agentiva_agents.llm.provider import CompletionRequest
from agentiva_agents.llm.stub import StubLlmProvider


async def _complete(context: dict) -> dict:
    provider = StubLlmProvider()
    request = CompletionRequest(
        system_prompt="",
        user_prompt="",
        response_schema={},
        context=context,
    )
    response = await provider.complete(request)
    return response.content


class TestSentimentRule:
    async def test_no_headlines_reports_zero_confidence(self) -> None:
        content = await _complete(
            {"agent_name": "sentiment_agent", "symbol": "BTCUSDT", "headlines": [],
             "news_source": "not_configured"}
        )
        assert content["confidence"] == "0"
        assert content["sentiment_score"] == "0"
        assert content["reason_codes"] == ["NO_NEWS_SOURCE_CONFIGURED"]

    async def test_an_unavailable_source_is_distinguished_from_not_configured(self) -> None:
        content = await _complete(
            {"agent_name": "sentiment_agent", "symbol": "BTCUSDT", "headlines": [],
             "news_source": "unavailable"}
        )
        assert content["reason_codes"] == ["NEWS_SOURCE_UNAVAILABLE"]

    async def test_bullish_headlines_score_positive(self) -> None:
        content = await _complete(
            {
                "agent_name": "sentiment_agent",
                "symbol": "BTCUSDT",
                "news_source": "cryptocompare",
                "headlines": [
                    {"title": "Bitcoin surges to a new record high", "source": "a"},
                    {"title": "Analysts bullish on crypto rally", "source": "b"},
                ],
            }
        )
        assert Decimal(content["sentiment_score"]) > 0
        assert content["reason_codes"] == ["BULLISH_HEADLINES_DOMINANT"]
        assert content["headline_count"] == 2
        assert set(content["sources"]) == {"a", "b"}
        assert Decimal(content["confidence"]) > 0

    async def test_bearish_headlines_score_negative(self) -> None:
        content = await _complete(
            {
                "agent_name": "sentiment_agent",
                "symbol": "BTCUSDT",
                "news_source": "cryptocompare",
                "headlines": [
                    {"title": "Exchange hack drains millions", "source": "a"},
                    {"title": "Bitcoin plunges amid sell-off", "source": "b"},
                ],
            }
        )
        assert Decimal(content["sentiment_score"]) < 0
        assert content["reason_codes"] == ["BEARISH_HEADLINES_DOMINANT"]

    async def test_mixed_headlines_are_neutral(self) -> None:
        content = await _complete(
            {
                "agent_name": "sentiment_agent",
                "symbol": "BTCUSDT",
                "news_source": "cryptocompare",
                "headlines": [
                    {"title": "Market closes flat for the week", "source": "a"},
                ],
            }
        )
        assert content["reason_codes"] == ["MIXED_OR_NEUTRAL_HEADLINES"]

    async def test_plain_string_headlines_are_tolerated(self) -> None:
        content = await _complete(
            {
                "agent_name": "sentiment_agent",
                "symbol": "BTCUSDT",
                "news_source": "cryptocompare",
                "headlines": ["Bitcoin surges to a new record high"],
            }
        )
        assert Decimal(content["sentiment_score"]) > 0


class TestMarketRuleOnChainNote:
    async def test_available_onchain_data_is_mentioned_in_notes(self) -> None:
        content = await _complete(
            {
                "agent_name": "market_agent",
                "symbol": "BTCUSDT",
                "indicators": {"EMA_12": "101000", "EMA_26": "99000", "ATR_14": "800",
                                "LAST_PRICE": "100000"},
                "get_onchain_metrics": {"available": True, "hash_rate_gh_s": 500000000.0,
                                        "difficulty": 70000000000000.0},
            }
        )
        assert "ONCHAIN_DATA_AVAILABLE" in content["reason_codes"]
        assert "On-chain" in content["notes"]

    async def test_unavailable_onchain_data_is_not_mentioned(self) -> None:
        content = await _complete(
            {
                "agent_name": "market_agent",
                "symbol": "ETHUSDT",
                "indicators": {"EMA_12": "101000", "EMA_26": "99000", "ATR_14": "800",
                                "LAST_PRICE": "100000"},
                "get_onchain_metrics": {"available": False},
            }
        )
        assert "ONCHAIN_DATA_AVAILABLE" not in content["reason_codes"]
        assert "On-chain" not in content["notes"]
