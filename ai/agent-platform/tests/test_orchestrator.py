"""End-to-end tests for the agent pipeline.

Run against the deterministic stub provider, which is exactly why the stub
exists: a pipeline backed by a live model produces different output on every run
and cannot be asserted against.
"""

from __future__ import annotations

from typing import Any

import pytest

from agentiva_agents.agents.analysis_agents import (
    MarketAgent,
    PortfolioAgent,
    SentimentAgent,
    TechnicalAgent,
)
from agentiva_agents.agents.strategy_agent import StrategyAgent
from agentiva_agents.llm.stub import StubLlmProvider
from agentiva_agents.models.analysis import TradeAction
from agentiva_agents.orchestrator.graph import AgentOrchestrator
from agentiva_agents.tools.registry import ToolRegistry


def build_registry(indicators: dict[str, Any], portfolio: dict[str, Any] | None = None) -> ToolRegistry:
    """Builds a registry whose tools return fixed data."""
    registry = ToolRegistry()

    async def get_market_data(symbol: str) -> dict[str, Any]:
        return {"symbol": symbol, "lastPrice": indicators.get("LAST_PRICE", "0")}

    async def get_candles(symbol: str, timeframe: str = "15m") -> dict[str, Any]:
        return {"symbol": symbol, "timeframe": timeframe, "candles": []}

    async def get_indicators(symbol: str, timeframe: str = "15m") -> dict[str, Any]:
        return {"symbol": symbol, "indicators": indicators}

    async def get_portfolio() -> dict[str, Any]:
        return portfolio or {"exposurePercent": "0", "openPositionCount": 0}

    async def get_positions() -> list[Any]:
        return []

    async def get_news(symbol: str | None = None) -> dict[str, Any]:
        return {"headlines": []}

    async def get_risk_policy() -> dict[str, Any]:
        return {"minConfidencePercent": "60"}

    async def get_onchain_metrics(symbol: str) -> dict[str, Any]:
        return {"available": False}

    registry.register("get_market_data", "Ticker.", get_market_data,
                      {"symbol": {"type": "string", "required": True}})
    registry.register("get_candles", "Candles.", get_candles,
                      {"symbol": {"type": "string", "required": True}})
    registry.register("get_indicators", "Indicators.", get_indicators,
                      {"symbol": {"type": "string", "required": True}})
    registry.register("get_portfolio", "Portfolio.", get_portfolio)
    registry.register("get_positions", "Positions.", get_positions)
    registry.register("get_news", "News.", get_news)
    registry.register("get_risk_policy", "Risk policy.", get_risk_policy)
    registry.register("get_onchain_metrics", "On-chain metrics.", get_onchain_metrics,
                      {"symbol": {"type": "string", "required": True}})
    registry.seal()

    return registry


def build_orchestrator(registry: ToolRegistry) -> AgentOrchestrator:
    provider = StubLlmProvider()

    return AgentOrchestrator(
        evidence_agents=[
            MarketAgent(provider, registry),
            TechnicalAgent(provider, registry),
            SentimentAgent(provider, registry),
            PortfolioAgent(provider, registry),
        ],  # type: ignore[arg-type]
        strategy_agent=StrategyAgent(provider, registry),
        timeout_seconds=30,
    )


#: Indicators describing a clean uptrend: fast EMA above slow, positive MACD,
#: RSI in the oversold band. The stub's rules should align on BUY.
BULLISH = {
    "EMA_12": "101000",
    "EMA_26": "99000",
    "RSI_14": "28",
    "MACD": "150",
    "ATR_14": "800",
    "LAST_PRICE": "100000",
}

#: Indicators with no agreement: flat EMAs and a neutral RSI.
CONFLICTED = {
    "EMA_12": "100000",
    "EMA_26": "100000",
    "RSI_14": "50",
    "MACD": "0",
    "ATR_14": "500",
    "LAST_PRICE": "100000",
}


class TestPipelineHappyPath:
    async def test_an_aligned_market_produces_a_buy_proposal(self) -> None:
        orchestrator = build_orchestrator(build_registry(BULLISH))

        result = await orchestrator.run("BTCUSDT", "15m", "corr-1")

        assert result.succeeded
        assert result.proposal is not None
        assert result.proposal.action is TradeAction.BUY

        # A stop below entry and a target above it, for a long.
        assert result.proposal.stop_loss is not None
        assert result.proposal.stop_loss < result.proposal.entry
        assert result.proposal.take_profit is not None
        assert result.proposal.take_profit > result.proposal.entry

    async def test_the_run_record_is_auditable(self) -> None:
        orchestrator = build_orchestrator(build_registry(BULLISH))

        result = await orchestrator.run("BTCUSDT", "15m", "corr-2")

        # Everything the Agent Run detail page needs to reconstruct the run.
        assert result.agent_run_id
        assert result.symbol == "BTCUSDT"
        assert result.timeframe == "15m"
        assert result.started_at and result.completed_at
        assert result.duration_ms >= 0
        assert result.model == "stub"
        assert result.correlation_id == "corr-2"

        # Each agent's conclusion is recorded individually, so a proposal can be
        # attributed to the evidence behind it.
        for agent in ("market_agent", "technical_agent", "sentiment_agent", "portfolio_agent"):
            assert agent in result.analyses, f"{agent} is missing from the run record"

        assert "strategy_agent" in result.analyses

        # Tool use is recorded for permission auditing.
        assert "get_indicators" in result.tools_used

    async def test_only_read_only_tools_are_used(self) -> None:
        registry = build_registry(BULLISH)
        orchestrator = build_orchestrator(registry)

        result = await orchestrator.run("BTCUSDT", "15m")

        # Every tool touched must be one the registry sanctioned.
        assert set(result.tools_used) <= set(registry.names)

    async def test_the_pipeline_is_deterministic_under_the_stub(self) -> None:
        orchestrator = build_orchestrator(build_registry(BULLISH))

        first = await orchestrator.run("BTCUSDT", "15m")
        second = await orchestrator.run("BTCUSDT", "15m")

        assert first.proposal is not None
        assert second.proposal is not None
        assert first.proposal.action == second.proposal.action
        assert first.proposal.entry == second.proposal.entry
        assert first.proposal.stop_loss == second.proposal.stop_loss


class TestPipelineStandsAside:
    async def test_conflicting_evidence_produces_a_hold(self) -> None:
        orchestrator = build_orchestrator(build_registry(CONFLICTED))

        result = await orchestrator.run("BTCUSDT", "15m")

        assert result.succeeded
        assert result.proposal is not None

        # Standing aside is the conservative default and a recorded decision.
        assert result.proposal.action is TradeAction.HOLD
        assert result.proposal.confidence == 0

    async def test_missing_data_produces_a_hold_not_a_guess(self) -> None:
        orchestrator = build_orchestrator(build_registry({}))

        result = await orchestrator.run("BTCUSDT", "15m")

        assert result.proposal is not None
        assert result.proposal.action is TradeAction.HOLD

        # An agent with no evidence must report zero confidence rather than
        # inventing a reading, because a proposal would otherwise cite evidence
        # that does not exist.
        market = result.analyses["market_agent"]
        assert market["confidence"] == "0"
        assert "INSUFFICIENT_DATA" in market["reason_codes"]


class TestPipelineResilience:
    async def test_a_failing_tool_does_not_fail_the_run(self) -> None:
        registry = ToolRegistry()

        async def get_market_data(symbol: str) -> dict[str, Any]:
            return {"symbol": symbol, "lastPrice": "100000"}

        async def get_indicators(symbol: str, timeframe: str = "15m") -> dict[str, Any]:
            raise RuntimeError("The indicator service is unavailable.")

        async def get_candles(symbol: str, timeframe: str = "15m") -> dict[str, Any]:
            return {"candles": []}

        async def get_portfolio() -> dict[str, Any]:
            return {"exposurePercent": "0", "openPositionCount": 0}

        async def get_positions() -> list[Any]:
            return []

        async def get_news(symbol: str | None = None) -> dict[str, Any]:
            return {"headlines": []}

        async def get_risk_policy() -> dict[str, Any]:
            return {}

        registry.register("get_market_data", "Ticker.", get_market_data,
                          {"symbol": {"type": "string", "required": True}})
        registry.register("get_indicators", "Indicators.", get_indicators,
                          {"symbol": {"type": "string", "required": True}})
        registry.register("get_candles", "Candles.", get_candles,
                          {"symbol": {"type": "string", "required": True}})
        registry.register("get_portfolio", "Portfolio.", get_portfolio)
        registry.register("get_positions", "Positions.", get_positions)
        registry.register("get_news", "News.", get_news)
        registry.register("get_risk_policy", "Risk policy.", get_risk_policy)
        registry.seal()

        result = await build_orchestrator(registry).run("BTCUSDT", "15m")

        # The run completes with partial evidence and a HOLD, rather than
        # aborting. An agent should be able to reason from what it has.
        assert result.succeeded
        assert result.proposal is not None
        assert result.proposal.action is TradeAction.HOLD

    async def test_a_timeout_is_a_failed_run_not_an_empty_proposal(self) -> None:
        import asyncio

        registry = build_registry(BULLISH)
        provider = StubLlmProvider()

        class SlowAgent(MarketAgent):
            async def gather_context(self, symbol: str, timeframe: str) -> dict[str, Any]:
                await asyncio.sleep(5)
                return await super().gather_context(symbol, timeframe)

        orchestrator = AgentOrchestrator(
            evidence_agents=[SlowAgent(provider, registry)],  # type: ignore[arg-type]
            strategy_agent=StrategyAgent(provider, registry),
            timeout_seconds=1,
        )

        result = await orchestrator.run("BTCUSDT", "15m")

        # A timeout must never surface as a successful run with no proposal:
        # that would be indistinguishable from a genuine HOLD.
        assert not result.succeeded
        assert result.proposal is None
        assert result.failure_reason is not None
        assert "timeout" in result.failure_reason.lower()
