"""The trading proposal: the AI platform's only output into the trading path.

A proposal is advisory. It is consumed by the Trading Service, which records an
intent and submits it to the deterministic risk gate; the gate derives its own
position size and may reject the proposal outright. Nothing in this module can
cause an order.

Note what a proposal deliberately does **not** contain: a quantity, a notional,
a leverage figure, or any other sizing instruction. Position size is derived
solely by the Risk Service from the portfolio's risk budget and the stop
distance. Omitting the field is the structural reason an agent cannot influence
position size — there is nowhere for it to put the number.
"""

from __future__ import annotations

from decimal import Decimal
from typing import Annotated, Self

from pydantic import BaseModel, ConfigDict, Field, model_validator

from agentiva_agents.models.analysis import Confidence, ReasonCode, TradeAction


class AgentContribution(BaseModel):
    """One agent's contribution to a combined proposal."""

    model_config = ConfigDict(extra="forbid", frozen=True)

    agent_name: str = Field(min_length=1, max_length=64)
    confidence: Confidence
    reason_codes: list[ReasonCode] = Field(default_factory=list, max_length=20)


class TradingProposal(BaseModel):
    """A trade the strategy agent recommends considering."""

    model_config = ConfigDict(extra="forbid", frozen=True, str_strip_whitespace=True)

    action: TradeAction
    symbol: str = Field(min_length=3, max_length=24, pattern=r"^[A-Z0-9]+$")
    confidence: Confidence

    #: Reference entry price. Decimal throughout: a float would lose precision
    #: on a value such as 100250.123456789012345678.
    entry: Annotated[Decimal, Field(gt=0)]

    #: Protective stop. Optional in the contract but required by the default
    #: risk policy, and without it the risk gate cannot size the position at all
    #: — the sizing formula has no denominator.
    stop_loss: Annotated[Decimal, Field(gt=0)] | None = None

    take_profit: Annotated[Decimal, Field(gt=0)] | None = None

    reason_codes: list[ReasonCode] = Field(default_factory=list, max_length=20)

    #: Risk observations, e.g. ``VOLATILITY_ACCEPTABLE``. Advisory: the
    #: deterministic gate makes the actual decision.
    risk_notes: list[ReasonCode] = Field(default_factory=list, max_length=20)

    contributions: list[AgentContribution] = Field(default_factory=list, max_length=10)

    @model_validator(mode="after")
    def _validate_protective_levels(self) -> Self:
        """Rejects protective levels on the wrong side of the entry.

        An inverted stop — a long whose stop sits above entry — is the dangerous
        case, because it is not obviously wrong downstream. The stop distance is
        still a positive number, so sizing succeeds and produces a plausible
        quantity whose protective order would trigger immediately in profit,
        leaving the real downside unprotected.

        The Risk Service checks this too. Both checks are deliberate: this one
        keeps a malformed proposal from being published at all, and that one is
        the authoritative gate.
        """
        if self.action is TradeAction.HOLD:
            # A hold carries no protective levels to validate.
            return self

        if self.stop_loss is not None:
            if self.action is TradeAction.BUY and self.stop_loss >= self.entry:
                raise ValueError(
                    f"A BUY proposal must have its stop-loss below the entry price "
                    f"(entry {self.entry}, stop {self.stop_loss})."
                )
            if self.action is TradeAction.SELL and self.stop_loss <= self.entry:
                raise ValueError(
                    f"A SELL proposal must have its stop-loss above the entry price "
                    f"(entry {self.entry}, stop {self.stop_loss})."
                )

        if self.take_profit is not None:
            if self.action is TradeAction.BUY and self.take_profit <= self.entry:
                raise ValueError(
                    f"A BUY proposal must have its take-profit above the entry price "
                    f"(entry {self.entry}, target {self.take_profit})."
                )
            if self.action is TradeAction.SELL and self.take_profit >= self.entry:
                raise ValueError(
                    f"A SELL proposal must have its take-profit below the entry price "
                    f"(entry {self.entry}, target {self.take_profit})."
                )

        return self


class AgentRunRequest(BaseModel):
    """Request to run the agent pipeline for one symbol."""

    model_config = ConfigDict(extra="forbid", str_strip_whitespace=True)

    symbol: str = Field(min_length=3, max_length=24, pattern=r"^[A-Z0-9]+$")
    timeframe: str = Field(default="15m", pattern=r"^\d+[mhdw]$")

    #: Agents to run. Defaults to the MVP set when omitted.
    agents: list[str] = Field(default_factory=list, max_length=10)

    #: Correlation identifier propagated from the caller, so an agent run can be
    #: traced alongside the trade it leads to.
    correlation_id: str = Field(default="", max_length=100)


class AgentRunResult(BaseModel):
    """The complete, auditable record of one pipeline run.

    Shaped to answer every question the Agent Run detail page asks: which agents
    ran, which tools they called, which model was used, what each concluded, how
    long it took, and what was proposed.
    """

    model_config = ConfigDict(extra="forbid")

    agent_run_id: str
    symbol: str
    timeframe: str
    started_at: str
    completed_at: str
    duration_ms: int

    #: Model identifier, or ``stub`` when the deterministic provider ran.
    model: str

    #: Read-only tools invoked across the run, for permission auditing.
    tools_used: list[str] = Field(default_factory=list)

    #: Each agent's validated output, keyed by agent name.
    analyses: dict[str, dict[str, object]] = Field(default_factory=dict)

    #: The proposal, when the pipeline produced one.
    proposal: TradingProposal | None = None

    succeeded: bool = True
    failure_reason: str | None = None

    #: Token usage for cost attribution. Zero in stub mode.
    input_tokens: int = 0
    output_tokens: int = 0

    correlation_id: str = ""
