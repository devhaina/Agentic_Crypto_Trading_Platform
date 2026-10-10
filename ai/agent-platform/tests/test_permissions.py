"""Tests for the AI trust boundary.

The platform's central security claim is that no agent can move funds. These
tests are what make that claim checkable rather than aspirational.
"""

from __future__ import annotations

import pytest

from agentiva_agents.policies.permissions import (
    FORBIDDEN_TOOL_NAMES,
    READ_ONLY_TOOL_NAMES,
    ToolPermissionError,
    assert_tool_is_permitted,
)
from agentiva_agents.tools.registry import ToolRegistry


class TestForbiddenTools:
    """Every execution capability must be refused."""

    @pytest.mark.parametrize("name", sorted(FORBIDDEN_TOOL_NAMES))
    def test_every_forbidden_tool_is_refused(self, name: str) -> None:
        with pytest.raises(ToolPermissionError):
            assert_tool_is_permitted(name)

    @pytest.mark.parametrize(
        "name",
        [
            # Variations an exact-name deny-list would miss. Without the
            # pattern checks each of these would be registrable.
            "place_order_v2",
            "submit_spot_order",
            "create_limit_order",
            "order_submit_fast",
            "cancel_all_orders",
            "execute_market_order",
            "withdraw_to_wallet",
            "transfer_between_accounts",
            "liquidate_all",
            "get_api_key",
            "read_api_secret",
            "fetch_private_key",
            "set_kill_switch",
            "disable_risk_policy",
            "update_trading_mode",
            "run_subprocess",
            "eval_expression",
        ],
    )
    def test_near_miss_names_are_refused(self, name: str) -> None:
        with pytest.raises(ToolPermissionError):
            assert_tool_is_permitted(name)

    def test_an_unlisted_tool_is_refused_even_if_harmless(self) -> None:
        # The allow-list is the final gate: a plausible-sounding read tool is
        # still refused until someone sanctions it deliberately.
        with pytest.raises(ToolPermissionError, match="not on the read-only allow-list"):
            assert_tool_is_permitted("get_something_useful")

    def test_an_empty_name_is_refused(self) -> None:
        with pytest.raises(ToolPermissionError):
            assert_tool_is_permitted("   ")


class TestAllowedTools:
    """Sanctioned read-only tools must be permitted."""

    @pytest.mark.parametrize("name", sorted(READ_ONLY_TOOL_NAMES))
    def test_every_allowed_tool_is_permitted(self, name: str) -> None:
        assert_tool_is_permitted(name)

    def test_no_allowed_tool_is_also_forbidden(self) -> None:
        # A name in both sets would make the policy's behaviour depend on check
        # order, which is exactly the kind of ambiguity a security gate must not
        # have.
        overlap = READ_ONLY_TOOL_NAMES & FORBIDDEN_TOOL_NAMES
        assert not overlap, f"These names appear in both lists: {sorted(overlap)}"


class TestToolRegistry:
    """The registry must enforce the policy at registration time."""

    async def test_registering_a_forbidden_tool_raises(self) -> None:
        registry = ToolRegistry()

        async def place_order(symbol: str, quantity: str) -> None:
            raise AssertionError("This must never be reachable.")

        with pytest.raises(ToolPermissionError):
            registry.register("place_order", "Places an order.", place_order)

        assert "place_order" not in registry.names

    async def test_registering_an_allowed_tool_succeeds(self) -> None:
        registry = ToolRegistry()

        async def get_portfolio() -> dict[str, str]:
            return {"status": "ok"}

        registry.register("get_portfolio", "Reads the portfolio.", get_portfolio)

        assert registry.names == ["get_portfolio"]

    async def test_a_synchronous_tool_is_refused(self) -> None:
        registry = ToolRegistry()

        def get_portfolio() -> dict[str, str]:
            return {}

        # A blocking tool would stall the event loop and hold every concurrent
        # agent run, so it is refused rather than merely discouraged.
        with pytest.raises(RuntimeError, match="async callable"):
            registry.register("get_portfolio", "Reads the portfolio.", get_portfolio)  # type: ignore[arg-type]

    async def test_a_sealed_registry_refuses_new_tools(self) -> None:
        registry = ToolRegistry()

        async def get_positions() -> list[str]:
            return []

        registry.seal()

        with pytest.raises(RuntimeError, match="sealed"):
            registry.register("get_positions", "Reads positions.", get_positions)

    async def test_requesting_an_unregistered_tool_raises(self) -> None:
        registry = ToolRegistry()

        with pytest.raises(ToolPermissionError):
            registry.get("get_market_data")

    async def test_duplicate_registration_is_refused(self) -> None:
        registry = ToolRegistry()

        async def get_news() -> dict[str, list[str]]:
            return {"headlines": []}

        registry.register("get_news", "Reads news.", get_news)

        with pytest.raises(RuntimeError, match="already registered"):
            registry.register("get_news", "Reads news again.", get_news)


class TestRegistryCompleteness:
    """The production registry must contain only sanctioned tools."""

    async def test_the_production_registry_passes_its_own_audit(self) -> None:
        from agentiva_agents.config import Settings
        from agentiva_agents.tools.market_tools import PlatformReadClient, register_market_tools
        from agentiva_agents.tools.news_tools import NewsClient, register_news_tools
        from agentiva_agents.tools.onchain_tools import OnChainClient, register_onchain_tools

        settings = Settings()
        read_client = PlatformReadClient(settings)
        news_client = NewsClient(settings)
        onchain_client = OnChainClient(settings)

        try:
            registry = ToolRegistry()
            register_market_tools(registry, read_client)
            register_news_tools(registry, news_client)
            register_onchain_tools(registry, onchain_client)

            # Must not raise: every registered tool is on the allow-list.
            registry.assert_complete()

            assert set(registry.names) <= READ_ONLY_TOOL_NAMES
        finally:
            await read_client.aclose()
            await news_client.aclose()
            await onchain_client.aclose()
