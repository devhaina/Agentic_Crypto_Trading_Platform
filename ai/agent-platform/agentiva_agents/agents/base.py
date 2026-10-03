"""Base class for every analysis agent."""

from __future__ import annotations

import abc
import time
from typing import Any, Generic, TypeVar

import structlog
from pydantic import BaseModel, ValidationError

from agentiva_agents.llm.provider import CompletionRequest, LlmProvider
from agentiva_agents.tools.registry import ToolRegistry

logger = structlog.get_logger(__name__)

TOutput = TypeVar("TOutput", bound=BaseModel)


class AgentExecutionError(RuntimeError):
    """Raised when an agent cannot produce valid output."""


class AgentResult(Generic[TOutput]):
    """An agent's validated output plus its run metadata."""

    __slots__ = ("output", "duration_ms", "model", "tools_used", "input_tokens", "output_tokens")

    def __init__(
        self,
        output: TOutput,
        duration_ms: int,
        model: str,
        tools_used: list[str],
        input_tokens: int,
        output_tokens: int,
    ) -> None:
        self.output = output
        self.duration_ms = duration_ms
        self.model = model
        self.tools_used = tools_used
        self.input_tokens = input_tokens
        self.output_tokens = output_tokens


class Agent(abc.ABC, Generic[TOutput]):
    """An analysis agent.

    Agents gather evidence using read-only tools, ask the provider for a
    structured conclusion, and validate it against a Pydantic contract. The
    validation step is not optional and is implemented here rather than in each
    agent, so no subclass can skip it.
    """

    #: Agent identity, used in logs, metrics and the audit record.
    name: str = "agent"

    #: Agent version, recorded on the run so a behaviour change is attributable.
    version: str = "1.0.0"

    #: Tools this agent is allowed to call. A subset of the registry.
    required_tools: tuple[str, ...] = ()

    def __init__(self, provider: LlmProvider, registry: ToolRegistry) -> None:
        self._provider = provider
        self._registry = registry

    @property
    @abc.abstractmethod
    def output_model(self) -> type[TOutput]:
        """The Pydantic contract this agent's output must satisfy."""

    @property
    @abc.abstractmethod
    def system_prompt(self) -> str:
        """The agent's role instruction."""

    @abc.abstractmethod
    def user_prompt(self, symbol: str, timeframe: str) -> str:
        """The per-run instruction."""

    async def gather_context(self, symbol: str, timeframe: str) -> dict[str, Any]:
        """Collects evidence using this agent's permitted tools.

        The default implementation calls each of :attr:`required_tools`. A tool
        failure is logged and skipped rather than aborting the run: an agent
        should be able to reason from partial evidence and report lower
        confidence, which is more useful than no analysis at all.
        """
        context: dict[str, Any] = {
            "agent_name": self.name,
            "symbol": symbol,
            "timeframe": timeframe,
            "tools_used": [],
        }

        for tool_name in self.required_tools:
            try:
                tool = self._registry.get(tool_name)
                result = await self._invoke(tool_name, tool, symbol, timeframe)
                context[tool_name] = result
                context["tools_used"].append(tool_name)
            except Exception as exc:  # noqa: BLE001 - a tool must never break a run
                logger.warning(
                    "agent_tool_failed", agent=self.name, tool=tool_name, error=str(exc)
                )

        return context

    async def _invoke(
        self, tool_name: str, tool: Any, symbol: str, timeframe: str
    ) -> Any:
        """Calls a tool with the arguments its signature accepts."""
        import inspect

        parameters = inspect.signature(tool.handler).parameters
        kwargs: dict[str, Any] = {}

        if "symbol" in parameters:
            kwargs["symbol"] = symbol
        if "timeframe" in parameters:
            kwargs["timeframe"] = timeframe

        _ = tool_name
        return await tool.handler(**kwargs)

    async def run(
        self,
        symbol: str,
        timeframe: str,
        extra_context: dict[str, Any] | None = None,
    ) -> AgentResult[TOutput]:
        """Runs the agent and returns validated output.

        Args:
            symbol: The trading pair to analyse.
            timeframe: The candle interval.
            extra_context: Additional context merged in before the completion.
                The orchestrator uses this to pass the evidence agents' output
                to the strategy agent. Passed as an argument rather than stored
                on the agent so that concurrent runs of the same instance
                cannot observe each other's evidence.

        Raises:
            AgentExecutionError: The provider failed, or its output did not
                satisfy the contract. Never returns unvalidated data.
        """
        started = time.perf_counter()

        context = await self.gather_context(symbol, timeframe)
        context = self.enrich_context(context)

        if extra_context:
            context.update(extra_context)

        request = CompletionRequest(
            system_prompt=self.system_prompt,
            user_prompt=self.user_prompt(symbol, timeframe),
            response_schema=self.output_model.model_json_schema(),
            context=context,
            tools=[],
        )

        try:
            response = await self._provider.complete(request)
        except Exception as exc:
            raise AgentExecutionError(
                f"Agent '{self.name}' failed to obtain a completion: {exc}"
            ) from exc

        payload = dict(response.content)

        # Identity is stamped rather than trusted from the model: one that names
        # itself something else must not be able to misattribute its output in
        # the audit record.
        #
        # Stamped only where the contract declares the field. Output models use
        # extra="forbid", and not every contract carries agent identity —
        # TradingProposal deliberately does not, because a proposal is a
        # statement about a trade rather than about its author. Writing the
        # field unconditionally would make every proposal fail validation.
        declared = self.output_model.model_fields

        if "agent_name" in declared:
            payload["agent_name"] = self.name

        if "agent_version" in declared:
            payload["agent_version"] = self.version

        if "symbol" in declared:
            payload.setdefault("symbol", symbol)

        try:
            output = self.output_model.model_validate(payload)
        except ValidationError as exc:
            # The hard boundary. Output that fails the contract is discarded, not
            # coerced: passing a partially understood object into the trading
            # path is exactly what structured validation exists to prevent.
            raise AgentExecutionError(
                f"Agent '{self.name}' produced output that failed schema validation and was "
                f"discarded: {exc.error_count()} error(s). First: {exc.errors()[0] if exc.errors() else 'unknown'}"
            ) from exc

        duration_ms = int((time.perf_counter() - started) * 1000)

        return AgentResult(
            output=output,
            duration_ms=duration_ms,
            model=response.model,
            tools_used=response.tools_used or context.get("tools_used", []),
            input_tokens=response.input_tokens,
            output_tokens=response.output_tokens,
        )

    def enrich_context(self, context: dict[str, Any]) -> dict[str, Any]:
        """Hook for an agent to reshape its context before the completion."""
        return context
