"""The analysis agents.

Each agent has a narrow remit, a fixed tool set and a Pydantic output contract.
Narrowness is deliberate: a single agent asked to do everything produces output
that is hard to validate and impossible to attribute, and the Agent Run detail
page needs to show which agent concluded what.
"""

from __future__ import annotations

from typing import Any

from agentiva_agents.agents.base import Agent
from agentiva_agents.prompts.boundaries import compose
from agentiva_agents.models.analysis import (
    MarketAnalysis,
    PortfolioAnalysis,
    ResearchFinding,
    SentimentAnalysis,
    TechnicalAnalysis,
)


def _flatten_indicators(context: dict[str, Any]) -> dict[str, Any]:
    """Lifts indicator values to the top level of the context.

    The stub provider's rules read ``context["indicators"]``, and the gateway
    returns them nested under the tool name. Normalising here keeps the rules
    simple and means a real model sees the same shape.
    """
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


class MarketAgent(Agent[MarketAnalysis]):
    """Classifies trend, volatility, regime and price structure."""

    name = "market_agent"
    version = "1.0.0"
    required_tools = ("get_market_data", "get_candles", "get_indicators")

    @property
    def output_model(self) -> type[MarketAnalysis]:
        return MarketAnalysis

    @property
    def system_prompt(self) -> str:
        return compose(
            (
            "You are a market structure analyst for a cryptocurrency trading platform.\n\n"
            "Classify the current market regime, trend direction and volatility for one symbol "
            "from the data provided.\n\n"
            "Report only what the data supports. If the evidence is insufficient, say so with a "
            "confidence of 0 and the reason code INSUFFICIENT_DATA — an invented regime is worse "
            "than an admitted gap, because a downstream proposal would cite it as evidence.\n\n"
            "You are describing conditions, not outcomes."
            )
        )

    def user_prompt(self, symbol: str, timeframe: str) -> str:
        return (
            f"Analyse the market structure for {symbol} on the {timeframe} timeframe. "
            f"Determine the regime, trend strength and volatility, and identify any clear "
            f"support and resistance levels."
        )

    def enrich_context(self, context: dict[str, Any]) -> dict[str, Any]:
        return _flatten_indicators(context)


class TechnicalAgent(Agent[TechnicalAnalysis]):
    """Reads indicator values and their implications."""

    name = "technical_agent"
    version = "1.0.0"
    required_tools = ("get_indicators", "get_candles", "get_market_data")

    @property
    def output_model(self) -> type[TechnicalAnalysis]:
        return TechnicalAnalysis

    @property
    def system_prompt(self) -> str:
        return compose(
            (
            "You are a technical analyst for a cryptocurrency trading platform.\n\n"
            "Interpret EMA, RSI, MACD, ATR, VWAP and volume readings for one symbol and state "
            "whether they favour buying, selling or standing aside.\n\n"
            "HOLD is a complete and often correct answer. Prefer it when indicators disagree: "
            "the cost of a missed trade is bounded, the cost of a bad one is not.\n\n"
            "Set confidence to reflect genuine agreement between indicators, not enthusiasm."
            )
        )

    def user_prompt(self, symbol: str, timeframe: str) -> str:
        return (
            f"Interpret the technical indicators for {symbol} on the {timeframe} timeframe "
            f"and state the action they support, with the reason codes that justify it."
        )

    def enrich_context(self, context: dict[str, Any]) -> dict[str, Any]:
        return _flatten_indicators(context)


class SentimentAgent(Agent[SentimentAnalysis]):
    """Assesses sentiment from approved sources."""

    name = "sentiment_agent"
    version = "1.0.0"
    required_tools = ("get_news",)

    @property
    def output_model(self) -> type[SentimentAnalysis]:
        return SentimentAnalysis

    @property
    def system_prompt(self) -> str:
        return compose(
            (
            "You are a market sentiment analyst for a cryptocurrency trading platform.\n\n"
            "Assess sentiment for one symbol using only the approved sources provided.\n\n"
            "If no sources are available, report a confidence of 0 and the reason code "
            "NO_NEWS_SOURCE_CONFIGURED. Do not infer sentiment from price action — that is the "
            "technical agent's remit, and double-counting it would make a weak signal look "
            "corroborated."
            )
        )

    def user_prompt(self, symbol: str, timeframe: str) -> str:
        _ = timeframe
        return f"Assess market sentiment for {symbol} from the approved sources provided."

    def enrich_context(self, context: dict[str, Any]) -> dict[str, Any]:
        news = context.get("get_news")
        if isinstance(news, dict):
            context["headlines"] = news.get("headlines", [])
        return context


class PortfolioAgent(Agent[PortfolioAnalysis]):
    """Observes current positions, exposure and P&L."""

    name = "portfolio_agent"
    version = "1.0.0"
    required_tools = ("get_portfolio", "get_positions")

    @property
    def output_model(self) -> type[PortfolioAnalysis]:
        return PortfolioAnalysis

    @property
    def system_prompt(self) -> str:
        return compose(
            (
            "You are a portfolio analyst for a cryptocurrency trading platform.\n\n"
            "Describe current positions, exposure, concentration and P&L, and raise any "
            "portfolio-level concerns as reason codes.\n\n"
            "Your observations are advisory. The deterministic Risk Service enforces the actual "
            "exposure and concentration limits, and it will reject a trade regardless of what "
            "you conclude here."
            )
        )

    def user_prompt(self, symbol: str, timeframe: str) -> str:
        _ = timeframe
        return (
            f"Describe the current portfolio state with particular attention to existing "
            f"exposure to {symbol}, and raise any concentration or exposure concerns."
        )

    def enrich_context(self, context: dict[str, Any]) -> dict[str, Any]:
        portfolio = context.get("get_portfolio")
        if isinstance(portfolio, dict):
            context["portfolio"] = portfolio
        return context


class ResearchAgent(Agent[ResearchFinding]):
    """Analyses historical strategy performance.

    Explicitly cannot modify a production strategy: it has no write tool, and
    its output contract carries no action field, so there is nothing in its
    response that could be executed even by mistake.
    """

    name = "research_agent"
    version = "1.0.0"
    required_tools = ("get_strategy_performance", "get_recent_signals")

    @property
    def output_model(self) -> type[ResearchFinding]:
        return ResearchFinding

    @property
    def system_prompt(self) -> str:
        return compose(
            (
            "You are a quantitative research analyst for a cryptocurrency trading platform.\n\n"
            "Analyse historical strategy performance and identify weaknesses: regime "
            "sensitivity, false signal rates, drawdown behaviour and parameter sensitivity.\n\n"
            "Your output is a recommendation for a human to consider. Frame findings as evidence "
            "and open questions, not as instructions.\n\n"
            "Be explicit about the limits of the data. Historical performance does not predict "
            "future returns, and a finding drawn from too few trades should say so."
            )
        )

    def user_prompt(self, symbol: str, timeframe: str) -> str:
        _ = timeframe
        return (
            f"Analyse historical strategy performance relevant to {symbol} and identify "
            f"weaknesses, drawdown behaviour and false signal patterns."
        )
