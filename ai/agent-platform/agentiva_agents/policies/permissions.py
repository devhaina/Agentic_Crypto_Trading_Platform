"""The AI trust boundary, enforced in code.

The platform's central security claim is that no AI agent can move funds. That
claim is only worth something if it is enforced rather than documented, so this
module is the single gate through which every tool must pass, and it fails
closed in three independent ways:

1.  :data:`FORBIDDEN_TOOL_NAMES` lists the capabilities that must never exist
    here. Registering one raises at import time, so the process cannot start.
2.  :data:`READ_ONLY_TOOL_NAMES` is an allow-list. A tool not on it is refused
    even if it looks harmless — new capabilities are a deliberate decision, not
    an accident of someone adding a function.
3.  Name inspection catches near-misses such as ``submit_order_v2`` or
    ``place_trade``, which neither list would otherwise mention.

The deeper point is that this module is a second line of defence, not the first.
Even a completely compromised agent platform cannot place an order, because it
holds no exchange credentials and the Execution Service accepts work only from a
risk-approved trading intent. This gate exists so that a mistake is caught at
startup rather than discovered from an exchange statement.
"""

from __future__ import annotations

import re
from typing import Final

__all__ = [
    "FORBIDDEN_TOOL_NAMES",
    "FORBIDDEN_NAME_PATTERNS",
    "READ_ONLY_TOOL_NAMES",
    "ToolPermissionError",
    "assert_tool_is_permitted",
]


class ToolPermissionError(RuntimeError):
    """Raised when a tool would breach the AI trust boundary.

    Deliberately a hard error rather than a warning or a filtered-out tool. A
    platform that silently drops a forbidden tool would start successfully with
    a security model different from the one its authors believed in.
    """


#: Capabilities that must never be reachable from an agent.
FORBIDDEN_TOOL_NAMES: Final[frozenset[str]] = frozenset(
    {
        "place_order",
        "submit_order",
        "create_order",
        "cancel_order",
        "amend_order",
        "modify_order",
        "close_position",
        "liquidate_position",
        "withdraw_funds",
        "transfer_funds",
        "deposit_funds",
        "change_exchange_credentials",
        "set_exchange_credentials",
        "read_exchange_credentials",
        "update_risk_policy",
        "disable_risk_checks",
        "set_trading_mode",
        "deactivate_kill_switch",
        "execute_sql",
        "run_shell",
    }
)

#: Patterns catching variations the explicit list would miss.
#:
#: Without these, ``place_order_v2`` or ``submit_spot_order`` would slip through
#: an exact-name check. The patterns are intentionally broad: a false positive is
#: a developer renaming a read-only tool, which costs a minute; a false negative
#: is an agent with order-placement capability.
FORBIDDEN_NAME_PATTERNS: Final[tuple[re.Pattern[str], ...]] = (
    re.compile(r"(place|submit|create|send|execute|amend|modify|cancel)_.*order", re.IGNORECASE),
    re.compile(r"order_.*(place|submit|create|send|execute|cancel)", re.IGNORECASE),
    re.compile(r"(withdraw|transfer|deposit|liquidate)", re.IGNORECASE),
    re.compile(r"(credential|api_?key|api_?secret|private_?key|password|token)", re.IGNORECASE),
    re.compile(r"(kill_?switch|risk_?polic|trading_?mode).*(set|update|disable|deactivate|write)",
               re.IGNORECASE),
    re.compile(r"(set|update|disable|deactivate|write).*(kill_?switch|risk_?polic|trading_?mode)",
               re.IGNORECASE),
    re.compile(r"(exec|eval|shell|subprocess|system)", re.IGNORECASE),
)

#: The complete set of capabilities agents may have. All are read-only.
READ_ONLY_TOOL_NAMES: Final[frozenset[str]] = frozenset(
    {
        "get_market_data",
        "get_candles",
        "get_indicators",
        "get_orderbook",
        "get_portfolio",
        "get_positions",
        "get_strategy_performance",
        "get_news",
        "get_recent_signals",
        "get_risk_policy",
    }
)


def assert_tool_is_permitted(name: str) -> None:
    """Validates a tool name against the trust boundary.

    Args:
        name: The tool name being registered.

    Raises:
        ToolPermissionError: The name is forbidden, matches a forbidden pattern,
            or is absent from the read-only allow-list.
    """
    normalised = name.strip().lower()

    if not normalised:
        raise ToolPermissionError("A tool must have a name.")

    # 1. Explicit deny-list.
    if normalised in FORBIDDEN_TOOL_NAMES:
        raise ToolPermissionError(
            f"Tool '{name}' is explicitly forbidden to AI agents. Execution capability lives "
            f"outside the AI trust boundary, in the Execution Service, which accepts work only "
            f"from a risk-approved trading intent."
        )

    # 2. Pattern deny-list, catching variations of the above.
    for pattern in FORBIDDEN_NAME_PATTERNS:
        if pattern.search(normalised):
            raise ToolPermissionError(
                f"Tool '{name}' matches the forbidden capability pattern "
                f"'{pattern.pattern}'. Agents are strictly read-only. If this is genuinely a "
                f"read-only tool, rename it so its purpose is unambiguous — a name that reads "
                f"like an execution capability is itself a hazard."
            )

    # 3. Allow-list. Reached only by a name that passed both deny checks, and
    #    still refused unless it was explicitly sanctioned.
    if normalised not in READ_ONLY_TOOL_NAMES:
        raise ToolPermissionError(
            f"Tool '{name}' is not on the read-only allow-list. Add it to READ_ONLY_TOOL_NAMES "
            f"only after confirming it cannot mutate state, move funds, or read a credential. "
            f"Permitted tools: {', '.join(sorted(READ_ONLY_TOOL_NAMES))}."
        )
