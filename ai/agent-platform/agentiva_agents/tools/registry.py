"""The read-only tool registry.

Every tool an agent can reach is registered here, and registration runs the
permission gate in :mod:`agentiva_agents.policies.permissions`. A forbidden tool
raises at import time, so a mistake stops the process rather than shipping.
"""

from __future__ import annotations

import inspect
from collections.abc import Awaitable, Callable
from dataclasses import dataclass, field
from typing import Any

import structlog

from agentiva_agents.policies.permissions import (
    READ_ONLY_TOOL_NAMES,
    ToolPermissionError,
    assert_tool_is_permitted,
)

logger = structlog.get_logger(__name__)

ToolCallable = Callable[..., Awaitable[Any]]


@dataclass(frozen=True, slots=True)
class ToolDefinition:
    """A registered read-only tool."""

    name: str
    description: str
    handler: ToolCallable
    parameters: dict[str, Any] = field(default_factory=dict)

    def to_schema(self) -> dict[str, Any]:
        """Returns the JSON-schema description passed to an LLM provider."""
        return {
            "name": self.name,
            "description": self.description,
            "input_schema": {
                "type": "object",
                "properties": self.parameters,
                "required": [
                    key
                    for key, spec in self.parameters.items()
                    if isinstance(spec, dict) and spec.get("required", False)
                ],
            },
        }


class ToolRegistry:
    """Holds the tools available to agents.

    Instances are created once at startup. The registry is intentionally not
    mutable at request time: a tool set that can change while the service runs
    cannot be reasoned about, and would make the startup permission check
    meaningless.
    """

    def __init__(self) -> None:
        self._tools: dict[str, ToolDefinition] = {}
        self._sealed = False

    def register(
        self,
        name: str,
        description: str,
        handler: ToolCallable,
        parameters: dict[str, Any] | None = None,
    ) -> None:
        """Registers a read-only tool.

        Args:
            name: Tool name. Must pass the permission gate.
            description: What the tool returns, shown to the model.
            handler: Async callable implementing the tool.
            parameters: JSON-schema properties for the tool's arguments.

        Raises:
            ToolPermissionError: The name breaches the AI trust boundary.
            RuntimeError: The registry is sealed, or the name is already taken.
        """
        if self._sealed:
            raise RuntimeError(
                "The tool registry is sealed. Tools must be registered at startup so that the "
                "permission check covers every tool an agent can ever reach."
            )

        # The trust boundary. Raises before the tool is reachable.
        assert_tool_is_permitted(name)

        if name in self._tools:
            raise RuntimeError(f"Tool '{name}' is already registered.")

        if not inspect.iscoroutinefunction(handler):
            raise RuntimeError(
                f"Tool '{name}' must be an async callable. A blocking tool would stall the "
                f"event loop and hold every concurrent agent run."
            )

        self._tools[name] = ToolDefinition(
            name=name,
            description=description,
            handler=handler,
            parameters=parameters or {},
        )

        logger.info("tool_registered", tool=name)

    def seal(self) -> None:
        """Closes the registry to further registration."""
        self._sealed = True
        logger.info("tool_registry_sealed", tool_count=len(self._tools), tools=sorted(self._tools))

    def get(self, name: str) -> ToolDefinition:
        """Returns a registered tool.

        Raises:
            ToolPermissionError: The tool is not registered. Phrased as a
                permission error on purpose — from an agent's perspective an
                unregistered tool and a forbidden one are the same thing, and a
                model that invents a tool name must get an unambiguous refusal
                rather than a "not found" it might retry around.
        """
        tool = self._tools.get(name)

        if tool is None:
            raise ToolPermissionError(
                f"Tool '{name}' is not available to agents. Available tools: "
                f"{', '.join(sorted(self._tools))}."
            )

        return tool

    @property
    def names(self) -> list[str]:
        """Registered tool names, sorted."""
        return sorted(self._tools)

    def schemas(self) -> list[dict[str, Any]]:
        """Tool schemas for an LLM provider."""
        return [tool.to_schema() for tool in self._tools.values()]

    def assert_complete(self) -> None:
        """Verifies the registry contains only sanctioned tools.

        A belt-and-braces check run at startup. Registration already validates
        each name, but asserting the whole set catches a tool added by a path
        that bypassed :meth:`register`.
        """
        unexpected = set(self._tools) - READ_ONLY_TOOL_NAMES

        if unexpected:
            raise ToolPermissionError(
                f"The registry contains tools that are not on the read-only allow-list: "
                f"{', '.join(sorted(unexpected))}."
            )
