"""Tests for the trading proposal contract."""

from __future__ import annotations

from decimal import Decimal

import pytest
from pydantic import ValidationError

from agentiva_agents.models.analysis import TradeAction
from agentiva_agents.models.proposals import TradingProposal


class TestProposalSizing:
    """A proposal must not be able to express a position size."""

    def test_a_proposal_has_no_quantity_field(self) -> None:
        # The structural reason an agent cannot influence position size: there
        # is nowhere in the contract to put one. Size is derived solely by the
        # Risk Service from the risk budget and the stop distance.
        forbidden = {
            "quantity",
            "size",
            "position_size",
            "notional",
            "leverage",
            "amount",
            "risk_percent",
        }

        present = forbidden & set(TradingProposal.model_fields)

        assert not present, (
            f"TradingProposal exposes sizing field(s) {sorted(present)}. Position size must be "
            f"derived only by the Risk Service."
        )

    def test_an_unknown_field_is_rejected(self) -> None:
        # extra="forbid": a model that invents a field has misunderstood the
        # contract, and silently dropping it would hide that.
        with pytest.raises(ValidationError):
            TradingProposal(
                action=TradeAction.BUY,
                symbol="BTCUSDT",
                confidence=Decimal("0.7"),
                entry=Decimal("100000"),
                stop_loss=Decimal("98000"),
                quantity=Decimal("5"),  # type: ignore[call-arg]
            )


class TestProtectiveLevelDirection:
    """Protective levels must sit on the correct side of the entry."""

    def test_a_valid_long_is_accepted(self) -> None:
        proposal = TradingProposal(
            action=TradeAction.BUY,
            symbol="BTCUSDT",
            confidence=Decimal("0.7"),
            entry=Decimal("100000"),
            stop_loss=Decimal("98000"),
            take_profit=Decimal("104000"),
        )

        assert proposal.action is TradeAction.BUY

    def test_a_valid_short_is_accepted(self) -> None:
        proposal = TradingProposal(
            action=TradeAction.SELL,
            symbol="BTCUSDT",
            confidence=Decimal("0.7"),
            entry=Decimal("100000"),
            stop_loss=Decimal("102000"),
            take_profit=Decimal("96000"),
        )

        assert proposal.action is TradeAction.SELL

    def test_an_inverted_stop_on_a_long_is_rejected(self) -> None:
        # The dangerous case: the stop distance is still positive, so sizing
        # downstream would succeed and produce a position whose protective
        # order triggers immediately in profit, leaving the real downside
        # unprotected. Only a direction check catches it.
        with pytest.raises(ValidationError, match="stop-loss below the entry"):
            TradingProposal(
                action=TradeAction.BUY,
                symbol="BTCUSDT",
                confidence=Decimal("0.7"),
                entry=Decimal("100000"),
                stop_loss=Decimal("102000"),
            )

    def test_an_inverted_stop_on_a_short_is_rejected(self) -> None:
        with pytest.raises(ValidationError, match="stop-loss above the entry"):
            TradingProposal(
                action=TradeAction.SELL,
                symbol="BTCUSDT",
                confidence=Decimal("0.7"),
                entry=Decimal("100000"),
                stop_loss=Decimal("98000"),
            )

    def test_an_inverted_target_on_a_long_is_rejected(self) -> None:
        with pytest.raises(ValidationError, match="take-profit above the entry"):
            TradingProposal(
                action=TradeAction.BUY,
                symbol="BTCUSDT",
                confidence=Decimal("0.7"),
                entry=Decimal("100000"),
                stop_loss=Decimal("98000"),
                take_profit=Decimal("97000"),
            )

    def test_a_hold_needs_no_protective_levels(self) -> None:
        # Standing aside is a complete, recorded decision.
        proposal = TradingProposal(
            action=TradeAction.HOLD,
            symbol="BTCUSDT",
            confidence=Decimal("0"),
            entry=Decimal("100000"),
        )

        assert proposal.stop_loss is None


class TestProposalValidation:
    """Field-level constraints."""

    @pytest.mark.parametrize("confidence", ["-0.1", "1.1", "2"])
    def test_confidence_outside_zero_to_one_is_rejected(self, confidence: str) -> None:
        with pytest.raises(ValidationError):
            TradingProposal(
                action=TradeAction.HOLD,
                symbol="BTCUSDT",
                confidence=Decimal(confidence),
                entry=Decimal("100000"),
            )

    @pytest.mark.parametrize("entry", ["0", "-1"])
    def test_a_non_positive_entry_is_rejected(self, entry: str) -> None:
        with pytest.raises(ValidationError):
            TradingProposal(
                action=TradeAction.HOLD,
                symbol="BTCUSDT",
                confidence=Decimal("0"),
                entry=Decimal(entry),
            )

    @pytest.mark.parametrize("symbol", ["BTC-USDT", "btc usdt", "B", ""])
    def test_a_malformed_symbol_is_rejected(self, symbol: str) -> None:
        with pytest.raises(ValidationError):
            TradingProposal(
                action=TradeAction.HOLD,
                symbol=symbol,
                confidence=Decimal("0"),
                entry=Decimal("100000"),
            )

    def test_a_lowercase_reason_code_is_rejected(self) -> None:
        # Reason codes are aggregated and alerted on, so they must be stable
        # tokens rather than prose.
        with pytest.raises(ValidationError):
            TradingProposal(
                action=TradeAction.HOLD,
                symbol="BTCUSDT",
                confidence=Decimal("0"),
                entry=Decimal("100000"),
                reason_codes=["trend is up"],
            )

    def test_prices_keep_full_decimal_precision(self) -> None:
        # A float would lose the trailing digits of this value outright.
        precise = Decimal("100250.123456789012345678")

        proposal = TradingProposal(
            action=TradeAction.HOLD,
            symbol="BTCUSDT",
            confidence=Decimal("0"),
            entry=precise,
        )

        assert proposal.entry == precise
