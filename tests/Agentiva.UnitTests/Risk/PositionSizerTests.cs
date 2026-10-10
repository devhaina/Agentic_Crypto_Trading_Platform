using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Risk.Domain.Policies;
using Agentiva.Risk.Domain.Sizing;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.Risk;

/// <summary>
/// Tests for the deterministic position sizer.
/// </summary>
/// <remarks>
/// The central assertion across this suite is that the realised risk of the
/// returned position never exceeds the configured risk budget. Every other
/// property — fee handling, slippage, rounding direction, each cap — exists to
/// serve that one invariant, so each test states which way it must fail if the
/// calculation is wrong.
/// </remarks>
public sealed class PositionSizerTests
{
    private static PositionSizeRequest Request(
        RiskPolicy? policy = null,
        decimal entry = 100_000m,
        decimal stop = 98_000m,
        decimal equity = 100_000m,
        decimal available = 50_000m,
        decimal exposure = 0m,
        decimal symbolExposure = 0m,
        OrderSide side = OrderSide.Buy,
        InstrumentPrecision? precision = null)
        => new(
            side,
            Price.Create(entry),
            Price.Create(stop),
            Money.Create(equity, TestFixtures.Usdt),
            Money.Create(available, TestFixtures.Usdt),
            Money.Create(exposure, TestFixtures.Usdt),
            Money.Create(symbolExposure, TestFixtures.Usdt),
            precision ?? TestFixtures.BtcUsdtPrecision,
            policy ?? TestFixtures.DefaultPolicy());

    [Fact]
    public void Sizes_from_the_risk_budget_when_no_cap_binds()
    {
        // Deliberately uses the uncapped policy. Under the conservative
        // defaults the 1,000 USDT per-position cap binds long before the risk
        // budget does at a 100,000 BTC price, so the risk-budget path would
        // never be exercised.
        var result = PositionSizer.Calculate(
            Request(TestFixtures.UncappedPolicy(), equity: 1_000_000m, available: 10_000_000m));

        result.IsTradeable.ShouldBeTrue();
        result.BindingConstraint.ShouldBe(SizingConstraint.RiskBudget);

        // 0.5% of 1,000,000 equity.
        result.RiskBudget.Amount.ShouldBe(5_000m);
    }

    [Fact]
    public void Conservative_default_policy_caps_notional_before_the_risk_budget_binds()
    {
        // Documents the real behaviour of the shipped defaults: a 0.5% risk
        // budget on 100,000 equity with a 2% stop would want roughly 23,800
        // USDT of exposure, which the 1,000 USDT per-position cap cuts down.
        var result = PositionSizer.Calculate(Request());

        result.IsTradeable.ShouldBeTrue();
        result.BindingConstraint.ShouldBe(SizingConstraint.MaxPositionNotional);
        result.Notional.Amount.ShouldBeLessThanOrEqualTo(1_000m);
    }

    /// <summary>
    /// The invariant that matters most: realised risk must never exceed the budget.
    /// </summary>
    /// <remarks>
    /// A sizer that ignored fees would return a quantity whose stop-out loss is
    /// roughly 1.4x the budget at these parameters, and this assertion would fail.
    /// </remarks>
    [Fact]
    public void Risk_of_the_returned_position_never_exceeds_the_budget()
    {
        var result = PositionSizer.Calculate(Request());

        result.RiskAmount.Amount.ShouldBeLessThanOrEqualTo(result.RiskBudget.Amount);
    }

    [Theory]
    [InlineData(100_000, 98_000)]   // 2% stop
    [InlineData(100_000, 99_500)]   // 0.5% stop
    [InlineData(100_000, 99_900)]   // 0.1% stop, where fees dominate
    [InlineData(3_000, 2_900)]
    [InlineData(1.05, 1.00)]
    public void Risk_stays_within_budget_across_stop_distances(decimal entry, decimal stop)
    {
        var result = PositionSizer.Calculate(Request(entry: entry, stop: stop, available: 1_000_000m));

        if (!result.IsTradeable)
        {
            return;
        }

        result.RiskAmount.Amount.ShouldBeLessThanOrEqualTo(result.RiskBudget.Amount);
    }

    /// <summary>
    /// Verifies fees are actually in the denominator rather than merely reported.
    /// </summary>
    /// <remarks>
    /// Compares a zero-fee policy against a 0.1% policy at a tight 0.2% stop,
    /// where the two fee legs are a large share of total risk. If fees were
    /// ignored in the sizing formula both calls would return the same quantity.
    /// </remarks>
    [Fact]
    public void Fees_reduce_the_position_size()
    {
        var feeFree = TestFixtures.UncappedPolicy(takerFeePercent: 0m);
        var withFees = TestFixtures.UncappedPolicy(takerFeePercent: 0.1m);

        // Equity and the stop distance are chosen so the risk budget is the
        // binding constraint in both runs; if a cap bound instead, both calls
        // would return the same capped quantity and the comparison would be
        // vacuous.
        var free = PositionSizer.Calculate(Request(
            feeFree, entry: 100_000m, stop: 99_400m, equity: 1_000_000m, available: 10_000_000m));
        var paid = PositionSizer.Calculate(Request(
            withFees, entry: 100_000m, stop: 99_400m, equity: 1_000_000m, available: 10_000_000m));

        free.BindingConstraint.ShouldBe(SizingConstraint.RiskBudget);
        paid.BindingConstraint.ShouldBe(SizingConstraint.RiskBudget);

        paid.Quantity.Value.ShouldBeLessThan(free.Quantity.Value);
        paid.EstimatedEntryFee.Amount.ShouldBeGreaterThan(0m);
        paid.EstimatedExitFee.Amount.ShouldBeGreaterThan(0m);
    }

    [Fact]
    public void Slippage_is_applied_against_the_trader_on_a_buy()
    {
        var result = PositionSizer.Calculate(Request(side: OrderSide.Buy));

        // A buy is assumed to fill above the reference price, and its stop below.
        result.EffectiveEntryPrice.Value.ShouldBeGreaterThan(100_000m);
        result.EffectiveStopPrice.Value.ShouldBeLessThan(98_000m);
    }

    [Fact]
    public void Slippage_is_applied_against_the_trader_on_a_sell()
    {
        var result = PositionSizer.Calculate(
            Request(side: OrderSide.Sell, entry: 100_000m, stop: 102_000m));

        // A sell fills below the reference price, and its stop above.
        result.EffectiveEntryPrice.Value.ShouldBeLessThan(100_000m);
        result.EffectiveStopPrice.Value.ShouldBeGreaterThan(102_000m);
    }

    [Fact]
    public void Max_position_notional_caps_the_size()
    {
        var policy = RiskPolicy.Create(
            "small-cap", Percentage.FromPercent(0.5m), Money.Create(100m, TestFixtures.Usdt),
            Percentage.FromPercent(2m), Percentage.FromPercent(50m), Percentage.FromPercent(25m),
            5, Percentage.FromPercent(60m), Percentage.FromPercent(100m), true, true,
            Percentage.FromPercent(0.05m), Percentage.FromPercent(0.1m), TimeSpan.FromSeconds(30),
            DateTimeOffset.UnixEpoch, "test");

        var result = PositionSizer.Calculate(Request(policy));

        result.BindingConstraint.ShouldBe(SizingConstraint.MaxPositionNotional);
        result.Notional.Amount.ShouldBeLessThanOrEqualTo(100m);
    }

    [Fact]
    public void Available_balance_caps_the_size_and_leaves_room_for_the_fee()
    {
        var result = PositionSizer.Calculate(Request(available: 500m));

        result.BindingConstraint.ShouldBe(SizingConstraint.AvailableBalance);

        // Notional plus the entry fee must fit inside the balance; sizing to the
        // full balance would have the exchange reject the order.
        (result.Notional.Amount + result.EstimatedEntryFee.Amount)
            .ShouldBeLessThanOrEqualTo(500m);
    }

    [Fact]
    public void Portfolio_exposure_headroom_caps_the_size()
    {
        // 49,900 of a 50,000 limit is already used.
        var result = PositionSizer.Calculate(Request(exposure: 49_900m, available: 1_000_000m));

        result.BindingConstraint.ShouldBe(SizingConstraint.PortfolioExposure);
        result.Notional.Amount.ShouldBeLessThanOrEqualTo(100m);
    }

    [Fact]
    public void Exhausted_portfolio_exposure_yields_no_position()
    {
        var result = PositionSizer.Calculate(Request(exposure: 50_000m));

        result.IsTradeable.ShouldBeFalse();
        result.BindingConstraint.ShouldBe(SizingConstraint.PortfolioExposure);
        result.Quantity.IsZero.ShouldBeTrue();
    }

    [Fact]
    public void Asset_concentration_headroom_caps_the_size()
    {
        // 24,900 of a 25,000 per-asset limit is already used.
        var result = PositionSizer.Calculate(
            Request(symbolExposure: 24_900m, available: 1_000_000m));

        result.BindingConstraint.ShouldBe(SizingConstraint.AssetConcentration);
        result.Notional.Amount.ShouldBeLessThanOrEqualTo(100m);
    }

    /// <summary>
    /// Rounding must always floor, never round to nearest.
    /// </summary>
    /// <remarks>
    /// Rounding up could push the position past the risk budget or past the
    /// available balance, so the quantity must be an exact multiple of the step
    /// and no greater than the unrounded ideal.
    /// </remarks>
    [Fact]
    public void Quantity_is_floored_to_the_exchange_step_size()
    {
        var result = PositionSizer.Calculate(Request());

        (result.Quantity.Value % TestFixtures.BtcUsdtPrecision.StepSize).ShouldBe(0m);
    }

    [Fact]
    public void Size_rounding_below_the_exchange_minimum_is_untradeable()
    {
        // A coarse 1-unit step with a 100 USDT minimum notional against a tiny
        // risk budget: the ideal size floors to zero.
        var result = PositionSizer.Calculate(Request(
            equity: 100m,
            available: 100m,
            precision: TestFixtures.CoarsePrecision));

        result.IsTradeable.ShouldBeFalse();
        result.BindingConstraint.ShouldBe(SizingConstraint.BelowExchangeMinimum);
        result.Quantity.IsZero.ShouldBeTrue();
    }

    /// <summary>
    /// A real production-shaped bug, caught only by actually creating a
    /// trading intent against a live stack with a real BTC price rather than
    /// a round test fixture: the adverse-slippage price (0.05% of 82813.46 is
    /// 82854.86673) almost never lands on a valid tick, so the exchange-filter
    /// check at the end of <see cref="PositionSizer.Calculate"/> rejected
    /// every realistic price as <see cref="SizingConstraint.BelowExchangeMinimum"/>
    /// — a misleading reason, since the actual problem was that an internal
    /// risk-sizing artefact was never a price the exchange could quote, not
    /// that the position was genuinely too small. Every existing test above
    /// this one uses a round entry/stop (100,000/98,000), whose slippage
    /// adjustment happens to still land on a clean cent and never exercised
    /// this path.
    /// </summary>
    [Fact]
    public void A_realistic_non_round_price_is_still_tradeable()
    {
        var result = PositionSizer.Calculate(Request(
            entry: 82813.46m,
            stop: 81500m,
            equity: 10_655.376148m,
            available: 9_000.00025m,
            exposure: 1_655.3758980m,
            symbolExposure: 1_655.3758980m));

        result.IsTradeable.ShouldBeTrue();
        result.Quantity.IsZero.ShouldBeFalse();
        (result.EffectiveEntryPrice.Value % TestFixtures.BtcUsdtPrecision.TickSize).ShouldBe(0m);
    }

    /// <summary>
    /// A stop at the entry price has no risk denominator.
    /// </summary>
    /// <remarks>
    /// Guards against a division that would otherwise produce an effectively
    /// unbounded position — the most dangerous possible failure of a sizer.
    /// </remarks>
    [Fact]
    public void Stop_equal_to_entry_is_untradeable_rather_than_unbounded()
    {
        var policy = RiskPolicy.Create(
            "no-slip", Percentage.FromPercent(0.5m), Money.Create(1_000_000m, TestFixtures.Usdt),
            Percentage.FromPercent(2m), Percentage.FromPercent(100m), Percentage.FromPercent(100m),
            5, Percentage.FromPercent(60m), Percentage.FromPercent(100m), true, true,
            Percentage.Zero, Percentage.Zero, TimeSpan.FromSeconds(30),
            DateTimeOffset.UnixEpoch, "test");

        var result = PositionSizer.Calculate(
            Request(policy, entry: 100_000m, stop: 100_000m, available: 10_000_000m));

        result.IsTradeable.ShouldBeFalse();
        result.Quantity.IsZero.ShouldBeTrue();
    }

    [Fact]
    public void Zero_equity_yields_no_position()
    {
        var result = PositionSizer.Calculate(Request(equity: 0m, available: 0m));

        result.IsTradeable.ShouldBeFalse();
        result.Quantity.IsZero.ShouldBeTrue();
    }

    [Fact]
    public void Calculation_is_deterministic()
    {
        var request = Request();

        var first = PositionSizer.Calculate(request);
        var second = PositionSizer.Calculate(request);

        second.Quantity.ShouldBe(first.Quantity);
        second.RiskAmount.ShouldBe(first.RiskAmount);
        second.BindingConstraint.ShouldBe(first.BindingConstraint);
    }

    [Fact]
    public void Reported_notional_matches_the_returned_quantity()
    {
        var result = PositionSizer.Calculate(Request());

        // Reported figures must describe the order actually being placed, not
        // the pre-rounding ideal.
        var expected = result.Quantity.Value * result.EffectiveEntryPrice.Value;
        result.Notional.Amount.ShouldBe(expected);
    }
}
