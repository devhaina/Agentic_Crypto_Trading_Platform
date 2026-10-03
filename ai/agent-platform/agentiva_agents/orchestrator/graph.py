"""The orchestration graph.

A small, explicit state machine rather than a general agent framework. The
pipeline has exactly two meaningful stages — gather evidence, then synthesise —
and expressing that directly keeps the control flow readable and the failure
behaviour obvious.
"""

from __future__ import annotations

import asyncio
import uuid
from datetime import UTC, datetime

import structlog

from agentiva_agents.agents.base import Agent, AgentExecutionError
from agentiva_agents.models.proposals import AgentRunResult, TradingProposal
from agentiva_agents.orchestrator.state import AgentRunState, PipelineStage

logger = structlog.get_logger(__name__)


class AgentOrchestrator:
    """Runs the agent pipeline for one symbol."""

    def __init__(
        self,
        evidence_agents: list[Agent[object]],
        strategy_agent: Agent[TradingProposal],
        timeout_seconds: int = 60,
    ) -> None:
        self._evidence_agents = evidence_agents
        self._strategy_agent = strategy_agent
        self._timeout_seconds = timeout_seconds

    async def run(
        self,
        symbol: str,
        timeframe: str = "15m",
        correlation_id: str = "",
    ) -> AgentRunResult:
        """Runs the pipeline and returns an auditable result."""
        agent_run_id = str(uuid.uuid4())
        started_at = datetime.now(UTC)

        state = AgentRunState(
            agent_run_id=agent_run_id,
            symbol=symbol.upper(),
            timeframe=timeframe,
            correlation_id=correlation_id,
        )

        log = logger.bind(
            agent_run_id=agent_run_id,
            symbol=state.symbol,
            timeframe=timeframe,
            correlation_id=correlation_id,
        )
        log.info("agent_run_started", agent_count=len(self._evidence_agents) + 1)

        try:
            state = await asyncio.wait_for(
                self._execute(state, log),
                timeout=self._timeout_seconds,
            )
        except TimeoutError:
            # A bounded run matters: the caller holds a request open, and an
            # unbounded model call would hold it indefinitely. A timeout is a
            # failed run, never a silently empty proposal.
            log.error("agent_run_timed_out", timeout_seconds=self._timeout_seconds)
            state = state.advance(PipelineStage.FAILED).with_failure(
                "orchestrator", f"The run exceeded its {self._timeout_seconds}s timeout."
            )

        completed_at = datetime.now(UTC)
        duration_ms = int((completed_at - started_at).total_seconds() * 1000)

        succeeded = state.stage is PipelineStage.COMPLETED

        result = AgentRunResult(
            agent_run_id=agent_run_id,
            symbol=state.symbol,
            timeframe=timeframe,
            started_at=started_at.isoformat(),
            completed_at=completed_at.isoformat(),
            duration_ms=duration_ms,
            model=state.model,
            tools_used=list(state.tools_used),
            analyses=state.analyses,
            proposal=state.proposal,
            succeeded=succeeded,
            failure_reason=self._summarise_failures(state) if state.failures else None,
            input_tokens=state.input_tokens,
            output_tokens=state.output_tokens,
            correlation_id=correlation_id,
        )

        log.info(
            "agent_run_completed",
            succeeded=succeeded,
            duration_ms=duration_ms,
            stage=state.stage,
            action=state.proposal.action if state.proposal else None,
            confidence=str(state.proposal.confidence) if state.proposal else None,
            failures=sorted(state.failures),
        )

        return result

    async def _execute(
        self, state: AgentRunState, log: structlog.BoundLogger
    ) -> AgentRunState:
        """Runs the two pipeline stages."""
        # --- Stage 1: gather evidence, concurrently -------------------------
        #
        # The evidence agents are independent — none reads another's output — so
        # they run concurrently. Running them sequentially would multiply the
        # run's latency by the number of agents for no benefit.
        state = state.advance(PipelineStage.GATHERING_EVIDENCE)

        results = await asyncio.gather(
            *(
                self._run_agent(agent, state.symbol, state.timeframe)
                for agent in self._evidence_agents
            ),
            return_exceptions=True,
        )

        for agent, outcome in zip(self._evidence_agents, results, strict=True):
            if isinstance(outcome, BaseException):
                # One agent failing does not fail the run. The strategy agent
                # proceeds with less evidence and should report lower
                # confidence, which the risk gate's minimum then enforces.
                log.warning("evidence_agent_failed", agent=agent.name, error=str(outcome))
                state = state.with_failure(agent.name, str(outcome))
                continue

            state = state.with_analysis(
                agent.name,
                outcome.output.model_dump(mode="json"),
                outcome.tools_used,
                outcome.input_tokens,
                outcome.output_tokens,
            )

            if outcome.model != "stub":
                state = state.advance(state.stage)

        # --- Stage 2: synthesise ---------------------------------------------
        state = state.advance(PipelineStage.SYNTHESISING)

        try:
            proposal_result = await self._run_strategy(state)
        except AgentExecutionError as exc:
            # Output that failed validation is discarded rather than coerced.
            # A malformed proposal must not reach the trading path at all.
            log.error("strategy_agent_failed", error=str(exc))
            return state.advance(PipelineStage.FAILED).with_failure(
                self._strategy_agent.name, str(exc)
            )

        state = state.with_analysis(
            self._strategy_agent.name,
            proposal_result.output.model_dump(mode="json"),
            proposal_result.tools_used,
            proposal_result.input_tokens,
            proposal_result.output_tokens,
        )

        state = state.with_proposal(proposal_result.output)

        return state.advance(PipelineStage.COMPLETED)

    async def _run_agent(self, agent: Agent[object], symbol: str, timeframe: str) -> object:
        """Runs one evidence agent."""
        return await agent.run(symbol, timeframe)

    async def _run_strategy(self, state: AgentRunState) -> object:
        """Runs the strategy agent with the gathered evidence in its context.

        The evidence is passed as an argument rather than attached to the agent
        instance. Agents are long-lived singletons, so mutating one to carry a
        particular run's state would let two concurrent runs see each other's
        evidence — and the resulting proposals would cite analyses from the
        wrong symbol.
        """
        return await self._strategy_agent.run(
            state.symbol,
            state.timeframe,
            extra_context={"analyses": state.analyses},
        )

    @staticmethod
    def _summarise_failures(state: AgentRunState) -> str:
        """Builds a single operator-facing failure summary."""
        return "; ".join(f"{agent}: {reason}" for agent, reason in sorted(state.failures.items()))
