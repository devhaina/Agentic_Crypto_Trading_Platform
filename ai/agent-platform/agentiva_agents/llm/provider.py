"""LLM provider abstraction.

The platform never depends on a specific vendor. An agent asks for a structured
completion and receives validated output; which model produced it is a
configuration choice, and the deterministic stub provider is a first-class
implementation rather than a test double.
"""

from __future__ import annotations

import abc
from dataclasses import dataclass, field
from typing import Any


@dataclass(frozen=True, slots=True)
class CompletionRequest:
    """A request for a structured completion."""

    system_prompt: str
    user_prompt: str

    #: JSON schema the response must satisfy. The provider is responsible for
    #: asking the model for this shape; the caller validates the result with
    #: Pydantic regardless, because a model can ignore the instruction.
    response_schema: dict[str, Any]

    max_tokens: int = 4096
    temperature: float = 0.0

    #: Read-only tool schemas the model may call.
    tools: list[dict[str, Any]] = field(default_factory=list)

    #: Structured context gathered from tools, embedded in the prompt.
    context: dict[str, Any] = field(default_factory=dict)


@dataclass(frozen=True, slots=True)
class CompletionResponse:
    """A structured completion."""

    #: The parsed JSON object. Still unvalidated against the domain contract.
    content: dict[str, Any]

    model: str
    input_tokens: int = 0
    output_tokens: int = 0

    #: Tools the provider invoked while producing the response.
    tools_used: list[str] = field(default_factory=list)


class LlmProvider(abc.ABC):
    """Produces a structured completion."""

    @property
    @abc.abstractmethod
    def model_name(self) -> str:
        """Identifier recorded on the agent run for auditing."""

    @abc.abstractmethod
    async def complete(self, request: CompletionRequest) -> CompletionResponse:
        """Produces a structured completion.

        Implementations must return parsed JSON or raise. They must never return
        free-form text for the caller to interpret: the whole point of the
        structured boundary is that no downstream code parses model prose.
        """
