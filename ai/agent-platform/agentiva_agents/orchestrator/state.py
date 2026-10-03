"""Orchestrator state.

The pipeline is modelled as an explicit state machine with a single, immutable
state object threaded through it. The alternative — agents calling each other —
produces a graph nobody can draw, where a failure in one agent silently changes
what another sees. Here every transition is a named node, and the state at each
step is inspectable, which is what makes the Agent Run detail page possible.
"""

from __future__ import annotations

from dataclasses import dataclass, field, replace
from enum import StrEnum
from typing import Any

from agentiva_agents.models.proposals import TradingProposal


class PipelineStage(StrEnum):
    """Nodes of the orchestration graph.

    ::

        PENDING
           |
           v
        GATHERING_EVIDENCE   (market, technical, sentiment, portfolio — concurrent)
           |
           v
        SYNTHESISING         (strategy agent combines the evidence)
           |
           +--> COMPLETED    (a validated proposal, or a recorded HOLD)
           |
           +--> FAILED       (an agent failed or produced invalid output)
    """

    PENDING = "PENDING"
    GATHERING_EVIDENCE = "GATHERING_EVIDENCE"
    SYNTHESISING = "SYNTHESISING"
    COMPLETED = "COMPLETED"
    FAILED = "FAILED"


@dataclass(frozen=True, slots=True)
class AgentRunState:
    """Immutable state of one pipeline run.

    Frozen on purpose. An orchestrator that mutates shared state in place makes
    concurrent agent execution unsafe and the run history unreconstructable;
    with a frozen state each transition produces a new value and the sequence is
    a record of exactly what happened.
    """

    agent_run_id: str
    symbol: str
    timeframe: str
    correlation_id: str
    stage: PipelineStage = PipelineStage.PENDING

    #: Each agent's validated output, keyed by agent name.
    analyses: dict[str, dict[str, Any]] = field(default_factory=dict)

    #: Read-only tools invoked across the run, for permission auditing.
    tools_used: tuple[str, ...] = ()

    proposal: TradingProposal | None = None

    #: Per-agent failures. A failed agent does not fail the run: the pipeline
    #: proceeds with the evidence it has and reports reduced confidence.
    failures: dict[str, str] = field(default_factory=dict)

    input_tokens: int = 0
    output_tokens: int = 0
    model: str = "stub"

    def advance(self, stage: PipelineStage) -> AgentRunState:
        """Returns a new state at the given stage."""
        return replace(self, stage=stage)

    def with_analysis(
        self,
        agent_name: str,
        output: dict[str, Any],
        tools: list[str],
        input_tokens: int,
        output_tokens: int,
    ) -> AgentRunState:
        """Returns a new state including one agent's output."""
        merged = dict(self.analyses)
        merged[agent_name] = output

        combined_tools = tuple(dict.fromkeys([*self.tools_used, *tools]))

        return replace(
            self,
            analyses=merged,
            tools_used=combined_tools,
            input_tokens=self.input_tokens + input_tokens,
            output_tokens=self.output_tokens + output_tokens,
        )

    def with_failure(self, agent_name: str, reason: str) -> AgentRunState:
        """Returns a new state recording an agent failure."""
        merged = dict(self.failures)
        merged[agent_name] = reason
        return replace(self, failures=merged)

    def with_proposal(self, proposal: TradingProposal) -> AgentRunState:
        """Returns a new state carrying the proposal."""
        return replace(self, proposal=proposal)
