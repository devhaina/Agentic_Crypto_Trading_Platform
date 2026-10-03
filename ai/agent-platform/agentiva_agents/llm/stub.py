"""A deterministic, rule-based provider.

The default. It exists for three reasons, in order of importance:

1.  The repository must run with no vendor account and no API key. A platform
    whose smoke test requires a paid credential is a platform nobody runs.
2.  Agent behaviour becomes reproducible, so the orchestrator can be tested with
    real assertions. A pipeline whose every run differs cannot be tested at all.
3.  It is a safe fallback when a configured provider has no key, so a
    misconfiguration degrades to deterministic analysis rather than taking the
    service down.

The rules below are simple, deliberately conservative technical heuristics. They
are *not* a trading strategy and make no claim to be profitable — the
deterministic strategy engine in the Strategy Service is the platform's actual
signal source. This provider's job is to exercise the pipeline end to end.
"""

from __future__ import annotations

from decimal import Decimal
from typing import Any

import structlog

from agentiva_agents.llm.provider import CompletionRequest, CompletionResponse, LlmProvider

logger = structlog.get_logger(__name__)


class StubLlmProvider(LlmProvider):
    """Derives analysis from indicator values with fixed rules."""

    @property
    def model_name(self) -> str:
        return "stub"

    async def complete(self, request: CompletionRequest) -> CompletionResponse:
        """Produces deterministic output from the request's context."""
        agent = str(request.context.get("agent_name", "unknown"))
        symbol = str(request.context.get("symbol", "UNKNOWN"))

        handlers = {
            "market_agent": self._market,
            "technical_agent": self._technical,
            "sentiment_agent": self._sentiment,
            "portfolio_agent": self._portfolio,
            "strategy_agent": self._strategy,
            "research_agent": self._research,
        }

        handler = handlers.get(agent)

        if handler is None:
            logger.warning("stub_unknown_agent", agent=agent)
            content: dict[str, Any] = {
                "agent_name": agent,
                "symbol": symbol,
                "confidence": "0",
                "reason_codes": ["STUB_NO_RULES_FOR_AGENT"],
            }
        else:
            content = handler(symbol, request.context)

        return CompletionResponse(
            content=content,
            model=self.model_name,
            # Zero: no tokens were consumed, and reporting a fabricated count
            # would corrupt cost attribution.
            input_tokens=0,
            output_tokens=0,
            tools_used=list(request.context.get("tools_used", [])),
        )

    # -- Per-agent rules ---------------------------------------------------

    @staticmethod
    def _indicator(context: dict[str, Any], name: str, default: Decimal) -> Decimal:
        """Reads an indicator value, tolerating absent or malformed data."""
        indicators = context.get("indicators") or {}

        if not isinstance(indicators, dict):
            return default

        raw = indicators.get(name)

        if raw is None:
            return default

        try:
            return Decimal(str(raw))
        except (ValueError, ArithmeticError):
            return default

    def _market(self, symbol: str, context: dict[str, Any]) -> dict[str, Any]:
        ema_fast = self._indicator(context, "EMA_12", Decimal(0))
        ema_slow = self._indicator(context, "EMA_26", Decimal(0))
        atr = self._indicator(context, "ATR_14", Decimal(0))
        last = self._indicator(context, "LAST_PRICE", Decimal(0))

        # With no data, report UNKNOWN at zero confidence rather than guessing.
        # An agent that invents a regime from nothing is worse than one that
        # admits it cannot tell.
        if ema_fast == 0 or ema_slow == 0:
            return {
                "agent_name": "market_agent",
                "symbol": symbol,
                "confidence": "0",
                "regime": "UNKNOWN",
                "volatility_percent": "0",
                "trend_strength": "0",
                "reason_codes": ["INSUFFICIENT_DATA"],
                "notes": "No indicator data was available for this symbol.",
            }

        spread = ema_fast - ema_slow
        trend = spread / ema_slow if ema_slow else Decimal(0)
        trend = max(Decimal(-1), min(Decimal(1), trend * Decimal(20)))

        volatility = (atr / last * Decimal(100)) if last else Decimal(0)
        volatility = max(Decimal(0), min(Decimal(1000), volatility * Decimal(20)))

        if trend > Decimal("0.2"):
            regime, codes = "TRENDING_UP", ["EMA_FAST_ABOVE_SLOW", "TREND_UP"]
        elif trend < Decimal("-0.2"):
            regime, codes = "TRENDING_DOWN", ["EMA_FAST_BELOW_SLOW", "TREND_DOWN"]
        elif volatility > Decimal(60):
            regime, codes = "VOLATILE", ["HIGH_VOLATILITY"]
        else:
            regime, codes = "RANGING", ["NO_CLEAR_TREND"]

        return {
            "agent_name": "market_agent",
            "symbol": symbol,
            "confidence": "0.6",
            "regime": regime,
            "volatility_percent": str(volatility.quantize(Decimal("0.01"))),
            "trend_strength": str(trend.quantize(Decimal("0.0001"))),
            "reason_codes": codes,
            "notes": "Deterministic rule-based regime classification.",
        }

    def _technical(self, symbol: str, context: dict[str, Any]) -> dict[str, Any]:
        rsi = self._indicator(context, "RSI_14", Decimal(0))
        ema_fast = self._indicator(context, "EMA_12", Decimal(0))
        ema_slow = self._indicator(context, "EMA_26", Decimal(0))
        macd = self._indicator(context, "MACD", Decimal(0))

        if rsi == 0 and ema_fast == 0:
            return {
                "agent_name": "technical_agent",
                "symbol": symbol,
                "confidence": "0",
                "indicators": {},
                "suggested_action": "HOLD",
                "reason_codes": ["INSUFFICIENT_DATA"],
                "notes": "No indicator data was available.",
            }

        codes: list[str] = []
        score = 0

        if ema_fast > ema_slow > 0:
            codes.append("EMA_CROSS_UP")
            score += 1
        elif 0 < ema_fast < ema_slow:
            codes.append("EMA_CROSS_DOWN")
            score -= 1

        if Decimal(0) < rsi < Decimal(30):
            codes.append("RSI_OVERSOLD")
            score += 1
        elif rsi > Decimal(70):
            codes.append("RSI_OVERBOUGHT")
            score -= 1

        if macd > 0:
            codes.append("MACD_POSITIVE")
            score += 1
        elif macd < 0:
            codes.append("MACD_NEGATIVE")
            score -= 1

        action = "BUY" if score >= 2 else "SELL" if score <= -2 else "HOLD"

        # Confidence scales with agreement between indicators and is capped
        # well below certainty. The default risk policy requires 60%, so a
        # single weak signal correctly fails to clear the gate.
        confidence = min(Decimal("0.8"), Decimal("0.2") * abs(score) + Decimal("0.2"))

        return {
            "agent_name": "technical_agent",
            "symbol": symbol,
            "confidence": str(confidence),
            "indicators": {
                "RSI_14": str(rsi),
                "EMA_12": str(ema_fast),
                "EMA_26": str(ema_slow),
                "MACD": str(macd),
            },
            "suggested_action": action,
            "reason_codes": codes or ["NO_SIGNAL"],
            "notes": f"Rule-based indicator score {score}.",
        }

    def _sentiment(self, symbol: str, context: dict[str, Any]) -> dict[str, Any]:
        headlines = context.get("headlines") or []
        count = len(headlines) if isinstance(headlines, list) else 0

        # No approved news source is wired until Phase 7. Reporting zero
        # confidence is the honest answer; a neutral reading presented
        # confidently would let a proposal cite evidence that does not exist.
        return {
            "agent_name": "sentiment_agent",
            "symbol": symbol,
            "confidence": "0" if count == 0 else "0.4",
            "sentiment_score": "0",
            "headline_count": count,
            "sources": [],
            "reason_codes": ["NO_NEWS_SOURCE_CONFIGURED"] if count == 0 else ["NEUTRAL_SENTIMENT"],
            "notes": "No approved news source is configured; sentiment is not assessed.",
        }

    def _portfolio(self, symbol: str, context: dict[str, Any]) -> dict[str, Any]:
        portfolio = context.get("portfolio") or {}

        def read(key: str) -> Decimal:
            try:
                return Decimal(str(portfolio.get(key, 0) or 0))
            except (ValueError, ArithmeticError):
                return Decimal(0)

        exposure = read("exposurePercent")
        positions = portfolio.get("openPositionCount") or 0

        concerns: list[str] = []

        if exposure > Decimal(40):
            concerns.append("EXPOSURE_HIGH")
        if isinstance(positions, int) and positions >= 4:
            concerns.append("POSITION_COUNT_HIGH")

        return {
            "agent_name": "portfolio_agent",
            "symbol": symbol,
            "confidence": "0.5",
            "open_position_count": positions if isinstance(positions, int) else 0,
            "total_exposure_percent": str(exposure),
            "largest_concentration_percent": "0",
            "realized_pnl": str(read("realizedPnl")),
            "unrealized_pnl": str(read("unrealizedPnl")),
            "concerns": concerns or ["PORTFOLIO_WITHIN_LIMITS"],
            "notes": "Deterministic portfolio observation.",
        }

    def _strategy(self, symbol: str, context: dict[str, Any]) -> dict[str, Any]:
        """Combines the upstream analyses into a proposal.

        Requires agreement between the market and technical agents before
        proposing a direction, and returns HOLD otherwise. Standing aside is a
        legitimate and recorded outcome, and the conservative default: the cost
        of a missed trade is bounded, the cost of a bad one is not.
        """
        analyses = context.get("analyses") or {}
        technical = analyses.get("technical_agent") or {}
        market = analyses.get("market_agent") or {}

        suggested = str(technical.get("suggested_action", "HOLD"))
        regime = str(market.get("regime", "UNKNOWN"))

        def confidence_of(block: dict[str, Any]) -> Decimal:
            try:
                return Decimal(str(block.get("confidence", 0) or 0))
            except (ValueError, ArithmeticError):
                return Decimal(0)

        technical_confidence = confidence_of(technical)
        market_confidence = confidence_of(market)

        last = self._indicator(context, "LAST_PRICE", Decimal(0))
        atr = self._indicator(context, "ATR_14", Decimal(0))

        aligned = (
            (suggested == "BUY" and regime == "TRENDING_UP")
            or (suggested == "SELL" and regime == "TRENDING_DOWN")
        )

        contributions = [
            {
                "agent_name": "market_agent",
                "confidence": str(market_confidence),
                "reason_codes": list(market.get("reason_codes") or []),
            },
            {
                "agent_name": "technical_agent",
                "confidence": str(technical_confidence),
                "reason_codes": list(technical.get("reason_codes") or []),
            },
        ]

        if not aligned or last <= 0:
            return {
                "action": "HOLD",
                "symbol": symbol,
                "confidence": "0",
                "entry": str(last) if last > 0 else "1",
                "reason_codes": ["AGENTS_NOT_ALIGNED"] if last > 0 else ["NO_PRICE_AVAILABLE"],
                "risk_notes": ["STANDING_ASIDE"],
                "contributions": contributions,
            }

        # Stop distance from ATR, with a floor so a quiet market cannot produce
        # a stop so tight that the position size becomes absurd.
        stop_distance = max(atr * Decimal(2), last * Decimal("0.005"))
        reward_distance = stop_distance * Decimal(2)

        if suggested == "BUY":
            stop = last - stop_distance
            target = last + reward_distance
        else:
            stop = last + stop_distance
            target = last - reward_distance

        if stop <= 0:
            return {
                "action": "HOLD",
                "symbol": symbol,
                "confidence": "0",
                "entry": str(last),
                "reason_codes": ["STOP_WOULD_BE_NON_POSITIVE"],
                "risk_notes": ["STANDING_ASIDE"],
                "contributions": contributions,
            }

        combined = (technical_confidence + market_confidence) / Decimal(2)

        return {
            "action": suggested,
            "symbol": symbol,
            "confidence": str(combined.quantize(Decimal("0.01"))),
            "entry": str(last.quantize(Decimal("0.01"))),
            "stop_loss": str(stop.quantize(Decimal("0.01"))),
            "take_profit": str(target.quantize(Decimal("0.01"))),
            "reason_codes": ["AGENTS_ALIGNED", f"REGIME_{regime}"],
            "risk_notes": ["STOP_DERIVED_FROM_ATR", "RISK_REWARD_1_TO_2"],
            "contributions": contributions,
        }

    def _research(self, symbol: str, context: dict[str, Any]) -> dict[str, Any]:
        _ = context
        return {
            "agent_name": "research_agent",
            "symbol": symbol,
            "confidence": "0",
            "strategy_name": "",
            "identified_weaknesses": ["INSUFFICIENT_HISTORY"],
            "max_drawdown_percent": "0",
            "false_signal_rate_percent": "0",
            "recommendations": [
                "Backtesting history is required before strategy weaknesses can be assessed."
            ],
            "notes": "The research agent requires Phase 8 backtesting data.",
        }
