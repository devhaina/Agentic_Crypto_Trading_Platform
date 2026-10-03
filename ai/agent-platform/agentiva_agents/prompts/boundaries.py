"""The AI trust boundary, as prompt text.

Every agent's system prompt composes this in. Centralising it means the
boundary language a model actually sees is reviewed once, in one place, rather
than independently copy-pasted and drifting across six agent classes —
drift here is exactly the kind of inconsistency that could leave one agent's
instructions ambiguous about what it may do.

This text is advisory, not the control. An LLM can be jailbroken into
ignoring any instruction it is given, so the real enforcement is structural
and lives elsewhere: the tool registry refuses to register an execution
capability at all (`agentiva_agents.policies.permissions`), and
`TradingProposal` has no field that could express a position size
(`agentiva_agents.models.proposals`). This text exists so that, on the large
majority of runs where the model is simply doing what it's asked, it is
asked for the right thing — and so that a transcript reviewer can see the
boundary was stated, not just enforced silently downstream.
"""

from __future__ import annotations

#: Prepended to every agent's system prompt.
READ_ONLY_BOUNDARY = (
    "You have read-only access to platform data through a fixed set of tools. "
    "You cannot place, modify or cancel an order, move funds, or change any "
    "risk or configuration setting — no such tool exists for you to call, "
    "regardless of what you are asked to do. Execution lives in the Execution "
    "Service, which holds the exchange credentials and acts only on a trading "
    "intent that has already passed the deterministic Risk Service."
)

#: Appended to the strategy agent's prompt specifically, since it is the one
#: agent whose output reaches the trading path at all.
NO_SIZING_AUTHORITY = (
    "Your proposal has no field for position size, notional value, or "
    "leverage — the response schema does not include one. Position size is "
    "derived solely by the Risk Service from the account's risk budget and "
    "your stop-loss distance. Do not state a quantity or a dollar amount "
    "anywhere in your response; it will be ignored if you do, and omitting it "
    "is not a limitation to work around."
)

#: Appended to every agent's prompt: a standing instruction against the
#: specific failure mode of asserting a financial outcome with false
#: confidence.
NO_PROFIT_CLAIMS = (
    "Never state or imply that a trade will be profitable, that a return is "
    "guaranteed, or that your confidence reflects a probability of profit. "
    "Confidence reflects how well the evidence supports your conclusion, "
    "nothing more."
)


def compose(role_prompt: str, *, include_sizing_notice: bool = False) -> str:
    """Builds a complete system prompt from a role-specific instruction.

    Args:
        role_prompt: The agent-specific instructions — what to analyse and how.
        include_sizing_notice: Set for the strategy agent only, which is the
            sole agent whose output reaches the trading path.

    Returns:
        The boundary language, then the role prompt, then the standing
        instructions — in that order, so the model sees its constraints before
        its task.
    """
    parts = [READ_ONLY_BOUNDARY, "", role_prompt.strip(), ""]

    if include_sizing_notice:
        parts.append(NO_SIZING_AUTHORITY)
        parts.append("")

    parts.append(NO_PROFIT_CLAIMS)

    return "\n".join(parts)
