"""The strategy agent: combines evidence into a trading proposal."""

from __future__ import annotations

from typing import Any

from agentiva_agents.agents.base import Agent
from agentiva_agents.models.proposals import TradingProposal


class StrategyAgent(Agent[TradingProposal]):
    """Produces a trading proposal from the other agents' analyses.

    The only agent whose output enters the trading path, and even then as a
    proposal: the Trading Service records an intent and the deterministic risk
    gate decides. Note that :class:`TradingProposal` has no quantity field, so
    this agent cannot express a position size at all.
    """

    name = "strategy_agent"
    version = "1.0.0"

    # Reads the risk policy so it can decline to propose a trade it knows would
    # be rejected — a read-only use that saves a pointless round trip.
    required_tools = ("get_market_data", "get_indicators", "get_risk_policy")

    @property
    def output_model(self) -> type[TradingProposal]:
        return TradingProposal

    @property
    def system_prompt(self) -> str:
        return (
            "You are the strategy coordinator for a cryptocurrency trading platform.\n\n"
            "Combine the analyses supplied by the market, technical, sentiment and portfolio "
            "agents into at most one trading proposal for the symbol.\n\n"
            "## What you decide\n"
            "- Direction: BUY, SELL or HOLD.\n"
            "- A reference entry price.\n"
            "- A protective stop-loss, which is mandatory for any BUY or SELL.\n"
            "- A take-profit target.\n"
            "- Reason codes and risk notes justifying the proposal.\n\n"
            "## What you do not decide\n"
            "- Position size, notional value or leverage. You have no field for these. The Risk "
            "Service derives size from the portfolio's risk budget and your stop distance, and "
            "your proposal cannot influence it.\n"
            "- Whether the trade happens. The deterministic risk gate decides, and it will "
            "reject your proposal if it breaches any limit.\n\n"
            "## How to decide\n"
            "Require genuine agreement between the agents before proposing a direction. Return "
            "HOLD when they disagree, when evidence is thin, or when conditions are unclear. "
            "HOLD is a recorded, legitimate decision and the correct default.\n\n"
            "Place the stop where the trade thesis is actually invalidated, not at a distance "
            "chosen to permit a larger position — you cannot affect the position size, and a "
            "stop placed for sizing reasons leaves the real risk unprotected.\n\n"
            "For a BUY the stop must be below the entry and the target above it; for a SELL the "
            "reverse. An inverted stop is rejected.\n\n"
            "Never assert that a trade will be profitable, and never promise a return. Set "
            "confidence to reflect the weight of evidence; a figure below the platform's minimum "
            "will correctly prevent the trade."
        )

    def user_prompt(self, symbol: str, timeframe: str) -> str:
        return (
            f"Review the agent analyses for {symbol} on the {timeframe} timeframe and produce "
            f"at most one trading proposal. Return HOLD if the evidence does not support a "
            f"directional trade."
        )

    def enrich_context(self, context: dict[str, Any]) -> dict[str, Any]:
        """Normalises indicator values for the proposal rules."""
        indicators: dict[str, Any] = {}

        raw = context.get("get_indicators")
        if isinstance(raw, dict):
            values = raw.get("indicators", raw)
            if isinstance(values, dict):
                indicators.update(values)

        ticker = context.get("get_market_data")
        if isinstance(ticker, dict):
            last = ticker.get("lastPrice") or ticker.get("last_price")
            if last is not None:
                indicators.setdefault("LAST_PRICE", last)

        context["indicators"] = indicators
        return context
