"""Structured agent output contracts.

Every agent returns one of these, validated by Pydantic before anything
downstream sees it. Free-form model text is never passed on: an LLM asked for
JSON will occasionally return prose, a trailing comma, or a confidently
hallucinated field, and the only safe place to discover that is at the boundary.
"""

from __future__ import annotations

from decimal import Decimal
from enum import StrEnum
from typing import Annotated

from pydantic import BaseModel, ConfigDict, Field, field_validator

# A confidence is always a fraction in [0, 1]. Named so the unit is unmistakable
# at every use site — the platform's .NET side converts it to an explicit
# Percentage on receipt.
Confidence = Annotated[Decimal, Field(ge=0, le=1)]

# A reason code is a stable upper-snake-case token, never a sentence. Codes are
# aggregated, filtered and alerted on, which prose cannot support.
ReasonCode = Annotated[str, Field(min_length=2, max_length=64, pattern=r"^[A-Z][A-Z0-9_]*$")]


class TradeAction(StrEnum):
    """What an agent recommends."""

    BUY = "BUY"
    SELL = "SELL"

    #: Explicitly stand aside. Modelled so that a decision not to trade is
    #: recorded and auditable rather than being the absence of a decision.
    HOLD = "HOLD"


class MarketRegime(StrEnum):
    """Coarse classification of current market conditions."""

    TRENDING_UP = "TRENDING_UP"
    TRENDING_DOWN = "TRENDING_DOWN"
    RANGING = "RANGING"
    VOLATILE = "VOLATILE"
    UNKNOWN = "UNKNOWN"


class AgentOutputBase(BaseModel):
    """Shared shape and validation settings for agent output."""

    model_config = ConfigDict(
        # Reject unknown fields. A model that invents a field has misunderstood
        # the contract, and silently dropping it would hide that.
        extra="forbid",
        frozen=True,
        str_strip_whitespace=True,
    )

    agent_name: str = Field(min_length=1, max_length=64)
    agent_version: str = Field(default="1.0.0", pattern=r"^\d+\.\d+\.\d+$")
    symbol: str = Field(min_length=3, max_length=24, pattern=r"^[A-Z0-9]+$")
    confidence: Confidence
    reason_codes: list[ReasonCode] = Field(default_factory=list, max_length=20)

    @field_validator("symbol", mode="before")
    @classmethod
    def _normalise_symbol(cls, value: str) -> str:
        return value.strip().upper() if isinstance(value, str) else value


class MarketAnalysis(AgentOutputBase):
    """Output of the market agent: trend, volatility and regime."""

    regime: MarketRegime = MarketRegime.UNKNOWN

    #: Annualised volatility as a percentage, e.g. 45.2 for 45.2%.
    volatility_percent: Annotated[Decimal, Field(ge=0, le=1000)] = Decimal(0)

    #: Trend strength in [-1, 1]; negative is downward.
    trend_strength: Annotated[Decimal, Field(ge=-1, le=1)] = Decimal(0)

    support_levels: list[Annotated[Decimal, Field(gt=0)]] = Field(default_factory=list, max_length=5)
    resistance_levels: list[Annotated[Decimal, Field(gt=0)]] = Field(
        default_factory=list, max_length=5
    )
    notes: str = Field(default="", max_length=2000)


class TechnicalAnalysis(AgentOutputBase):
    """Output of the technical agent: indicator readings and their reading."""

    #: Indicator values keyed by name, e.g. {"RSI_14": 61.2, "EMA_50": 99820.5}.
    indicators: dict[str, Decimal] = Field(default_factory=dict)

    suggested_action: TradeAction = TradeAction.HOLD
    notes: str = Field(default="", max_length=2000)


class SentimentAnalysis(AgentOutputBase):
    """Output of the sentiment agent."""

    #: Net sentiment in [-1, 1]; negative is bearish.
    sentiment_score: Annotated[Decimal, Field(ge=-1, le=1)] = Decimal(0)

    headline_count: Annotated[int, Field(ge=0, le=1000)] = 0

    #: Sources consulted, retained so a proposal's evidence is auditable.
    sources: list[str] = Field(default_factory=list, max_length=20)
    notes: str = Field(default="", max_length=2000)


class PortfolioAnalysis(AgentOutputBase):
    """Output of the portfolio agent."""

    open_position_count: Annotated[int, Field(ge=0)] = 0
    total_exposure_percent: Annotated[Decimal, Field(ge=0, le=1000)] = Decimal(0)
    largest_concentration_percent: Annotated[Decimal, Field(ge=0, le=1000)] = Decimal(0)
    realized_pnl: Decimal = Decimal(0)
    unrealized_pnl: Decimal = Decimal(0)

    #: Portfolio-level cautions, e.g. ``CONCENTRATION_HIGH``. Advisory only;
    #: the deterministic risk gate enforces the actual limits.
    concerns: list[ReasonCode] = Field(default_factory=list, max_length=20)
    notes: str = Field(default="", max_length=2000)


class ResearchFinding(AgentOutputBase):
    """Output of the research agent.

    The research agent analyses historical performance and never modifies a
    production strategy. Its output is a recommendation for a human to act on,
    which is why it carries no action field at all — there is nothing here that
    could be mistaken for an instruction.
    """

    strategy_name: str = Field(default="", max_length=100)
    identified_weaknesses: list[ReasonCode] = Field(default_factory=list, max_length=20)
    max_drawdown_percent: Annotated[Decimal, Field(ge=0, le=100)] = Decimal(0)
    false_signal_rate_percent: Annotated[Decimal, Field(ge=0, le=100)] = Decimal(0)
    recommendations: list[str] = Field(default_factory=list, max_length=20)
    notes: str = Field(default="", max_length=4000)
