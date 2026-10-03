using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Risk.Domain.Evaluation;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.Risk;

/// <summary>
/// Tests for the deterministic risk gate.
/// </summary>
/// <remarks>
/// Each test starts from <see cref="TestFixtures.ValidRequest"/> — which passes
/// every check — and changes exactly one field. A resulting rejection is
/// therefore attributable to that field alone.
/// </remarks>
public sealed class DeterministicRiskEvaluatorTests
{
    [Fact]
    public void Approves_a_request_that_satisfies_every_limit()
    {
        var result = DeterministicRiskEvaluator.Evaluate(TestFixtures.ValidRequest());

        result.IsApproved.ShouldBeTrue();
        result.RejectionCodes.ShouldBeEmpty();
        result.ApprovedQuantity.IsZero.ShouldBeFalse();
        result.Checks.ShouldAllBe(c => c.Passed);
    }

    [Fact]
    public void Approved_quantity_never_exceeds_the_risk_budget()
    {
        var result = DeterministicRiskEvaluator.Evaluate(TestFixtures.ValidRequest());

        result.Sizing.ShouldNotBeNull();
        result.Sizing!.RiskAmount.Amount.ShouldBeLessThanOrEqualTo(result.Sizing.RiskBudget.Amount);
    }

    // -------------------------------------------------------------------------
    // Integrity gates: these must short-circuit without sizing anything.
    // -------------------------------------------------------------------------

    [Fact]
    public void Kill_switch_rejects_and_performs_no_sizing()
    {
        var request = TestFixtures.ValidRequest() with { IsKillSwitchEngaged = true };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeFalse();
        result.RejectionCodes.ShouldContain(RiskCheckCodes.KillSwitch);

        // No size is computed: the kill switch means no order may exist at all.
        result.Sizing.ShouldBeNull();
        result.ApprovedQuantity.IsZero.ShouldBeTrue();
    }

    [Fact]
    public void Disabled_trading_rejects()
    {
        var request = TestFixtures.ValidRequest() with { IsTradingEnabled = false };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeFalse();
        result.RejectionCodes.ShouldContain(RiskCheckCodes.TradingDisabled);
        result.Sizing.ShouldBeNull();
    }

    [Fact]
    public void Unavailable_exchange_rejects()
    {
        var request = TestFixtures.ValidRequest() with { IsExchangeAvailable = false };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeFalse();
        result.RejectionCodes.ShouldContain(RiskCheckCodes.ExchangeUnavailable);
        result.Sizing.ShouldBeNull();
    }

    /// <summary>
    /// Stale market data must reject before any sizing happens.
    /// </summary>
    /// <remarks>
    /// Sizing from a stale price produces a confident number derived from a
    /// market that has already moved — the stop distance no longer reflects
    /// reality, so the risk budget no longer bounds anything.
    /// </remarks>
    [Fact]
    public void Stale_market_data_rejects_before_sizing()
    {
        var request = TestFixtures.ValidRequest() with { MarketDataAge = TimeSpan.FromMinutes(5) };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeFalse();
        result.RejectionCodes.ShouldContain(RiskCheckCodes.MarketDataStale);
        result.Sizing.ShouldBeNull();
    }

    [Fact]
    public void Backtest_mode_cannot_use_the_live_trading_path()
    {
        var request = TestFixtures.ValidRequest() with { TradingMode = TradingMode.Backtest };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeFalse();
        result.RejectionCodes.ShouldContain(RiskCheckCodes.TradingMode);
    }

    // -------------------------------------------------------------------------
    // Signal quality
    // -------------------------------------------------------------------------

    [Fact]
    public void Confidence_below_the_minimum_rejects()
    {
        var request = TestFixtures.ValidRequest() with { Confidence = Percentage.FromPercent(30m) };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeFalse();
        result.RejectionCodes.ShouldContain(RiskCheckCodes.MinConfidence);
    }

    [Fact]
    public void Missing_stop_loss_rejects()
    {
        var request = TestFixtures.ValidRequest() with { StopLoss = null };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeFalse();
        result.RejectionCodes.ShouldContain(RiskCheckCodes.StopLossRequired);

        // Without a stop there is no denominator, so sizing cannot run.
        result.Sizing.ShouldBeNull();
    }

    [Fact]
    public void Missing_take_profit_rejects_when_the_policy_requires_one()
    {
        var request = TestFixtures.ValidRequest() with { TakeProfit = null };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeFalse();
        result.RejectionCodes.ShouldContain(RiskCheckCodes.TakeProfitRequired);
    }

    /// <summary>
    /// An inverted stop must be rejected explicitly.
    /// </summary>
    /// <remarks>
    /// This is the subtle one. A long with its stop <em>above</em> entry still
    /// produces a positive stop distance, so the sizing arithmetic succeeds and
    /// returns a plausible quantity. The resulting position would have its
    /// protective order trigger immediately in profit while the actual downside
    /// ran completely unprotected. Only a direction check catches it.
    /// </remarks>
    [Fact]
    public void Stop_loss_above_entry_on_a_buy_rejects()
    {
        var request = TestFixtures.ValidRequest() with
        {
            Side = OrderSide.Buy,
            EntryPrice = Price.Create(100_000m),
            StopLoss = Price.Create(102_000m)
        };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeFalse();
        result.RejectionCodes.ShouldContain(RiskCheckCodes.StopLossDirection);
    }

    [Fact]
    public void Stop_loss_below_entry_on_a_sell_rejects()
    {
        var request = TestFixtures.ValidRequest() with
        {
            Side = OrderSide.Sell,
            EntryPrice = Price.Create(100_000m),
            StopLoss = Price.Create(98_000m),
            TakeProfit = Price.Create(96_000m)
        };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeFalse();
        result.RejectionCodes.ShouldContain(RiskCheckCodes.StopLossDirection);
    }

    [Fact]
    public void Take_profit_on_the_wrong_side_rejects()
    {
        var request = TestFixtures.ValidRequest() with
        {
            Side = OrderSide.Buy,
            EntryPrice = Price.Create(100_000m),
            StopLoss = Price.Create(98_000m),
            TakeProfit = Price.Create(97_000m)
        };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeFalse();
        result.RejectionCodes.ShouldContain(RiskCheckCodes.TakeProfitDirection);
    }

    [Fact]
    public void A_valid_short_is_approved()
    {
        var request = TestFixtures.ValidRequest() with
        {
            Side = OrderSide.Sell,
            EntryPrice = Price.Create(100_000m),
            StopLoss = Price.Create(102_000m),
            TakeProfit = Price.Create(96_000m)
        };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // Portfolio limits
    // -------------------------------------------------------------------------

    [Fact]
    public void Volatility_above_the_maximum_rejects()
    {
        // A policy with a 50% volatility ceiling, against an observed 90%.
        var lowVolatilityPolicy = Agentiva.Risk.Domain.Policies.RiskPolicy.Create(
            name: "low-vol",
            maxRiskPerTrade: Percentage.FromPercent(0.5m),
            maxPositionNotional: Money.Create(1_000m, TestFixtures.Usdt),
            maxDailyLoss: Percentage.FromPercent(2m),
            maxPortfolioExposure: Percentage.FromPercent(50m),
            maxAssetConcentration: Percentage.FromPercent(25m),
            maxOpenPositions: 5,
            minConfidence: Percentage.FromPercent(60m),
            maxVolatility: Percentage.FromPercent(50m),
            requireStopLoss: true,
            requireTakeProfit: true,
            slippageAssumption: Percentage.FromPercent(0.05m),
            takerFee: Percentage.FromPercent(0.1m),
            marketDataStalenessThreshold: TimeSpan.FromSeconds(30),
            now: DateTimeOffset.UnixEpoch,
            updatedBy: "test");

        var request = TestFixtures.ValidRequest() with
        {
            Policy = lowVolatilityPolicy,
            SymbolVolatility = Percentage.FromPercent(90m)
        };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeFalse();
        result.RejectionCodes.ShouldContain(RiskCheckCodes.MaxVolatility);
    }

    [Fact]
    public void Volatility_within_the_maximum_is_allowed()
    {
        var request = TestFixtures.ValidRequest() with
        {
            SymbolVolatility = Percentage.FromPercent(40m)
        };

        DeterministicRiskEvaluator.Evaluate(request).IsApproved.ShouldBeTrue();
    }

    [Fact]
    public void Reaching_the_open_position_limit_rejects()
    {
        var request = TestFixtures.ValidRequest() with { OpenPositionCount = 5 };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeFalse();
        result.RejectionCodes.ShouldContain(RiskCheckCodes.MaxOpenPositions);
    }

    /// <summary>
    /// The daily loss limit compares against loss already incurred.
    /// </summary>
    /// <remarks>
    /// Once the day's budget is spent the platform stops for the day. Sizing
    /// "one more trade that just fits" would defeat the purpose of a daily cap,
    /// which is to end a bad session rather than to ration it.
    /// </remarks>
    [Fact]
    public void Reaching_the_daily_loss_limit_rejects()
    {
        // 2% of 100,000 equity is a 2,000 limit.
        var request = TestFixtures.ValidRequest() with
        {
            DailyPnl = Money.Create(-2_000m, TestFixtures.Usdt)
        };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeFalse();
        result.RejectionCodes.ShouldContain(RiskCheckCodes.MaxDailyLoss);
    }

    [Fact]
    public void A_loss_within_the_daily_limit_is_allowed()
    {
        var request = TestFixtures.ValidRequest() with
        {
            DailyPnl = Money.Create(-500m, TestFixtures.Usdt)
        };

        DeterministicRiskEvaluator.Evaluate(request).IsApproved.ShouldBeTrue();
    }

    [Fact]
    public void A_profitable_day_cannot_breach_the_loss_limit()
    {
        var request = TestFixtures.ValidRequest() with
        {
            DailyPnl = Money.Create(50_000m, TestFixtures.Usdt)
        };

        DeterministicRiskEvaluator.Evaluate(request).IsApproved.ShouldBeTrue();
    }

    [Fact]
    public void A_duplicate_open_order_rejects()
    {
        var request = TestFixtures.ValidRequest() with { HasDuplicateOpenOrder = true };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeFalse();
        result.RejectionCodes.ShouldContain(RiskCheckCodes.DuplicateOrder);
    }

    [Fact]
    public void Exhausted_portfolio_exposure_rejects()
    {
        var request = TestFixtures.ValidRequest() with
        {
            CurrentExposure = Money.Create(50_000m, TestFixtures.Usdt)
        };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeFalse();
        result.RejectionCodes.ShouldContain(RiskCheckCodes.MaxPortfolioExposure);
    }

    // -------------------------------------------------------------------------
    // Reporting behaviour
    // -------------------------------------------------------------------------

    /// <summary>
    /// A rejection must report every failing check, not only the first.
    /// </summary>
    /// <remarks>
    /// Short-circuiting would be marginally faster and considerably less useful:
    /// an operator would fix one limit, retry, and discover the next.
    /// </remarks>
    [Fact]
    public void All_failing_checks_are_reported_not_just_the_first()
    {
        var request = TestFixtures.ValidRequest() with
        {
            Confidence = Percentage.FromPercent(10m),
            OpenPositionCount = 5,
            TakeProfit = null,
            HasDuplicateOpenOrder = true
        };

        var result = DeterministicRiskEvaluator.Evaluate(request);

        result.IsApproved.ShouldBeFalse();
        result.RejectionCodes.ShouldContain(RiskCheckCodes.MinConfidence);
        result.RejectionCodes.ShouldContain(RiskCheckCodes.MaxOpenPositions);
        result.RejectionCodes.ShouldContain(RiskCheckCodes.TakeProfitRequired);
        result.RejectionCodes.ShouldContain(RiskCheckCodes.DuplicateOrder);
        result.RejectionCodes.Count.ShouldBeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void Every_check_records_an_outcome_for_the_audit_trail()
    {
        var result = DeterministicRiskEvaluator.Evaluate(TestFixtures.ValidRequest());

        // The audit record must be able to show what ran, not just the verdict.
        result.Checks.Count.ShouldBeGreaterThanOrEqualTo(12);
        result.Checks.ShouldAllBe(c => !string.IsNullOrWhiteSpace(c.CheckName));
        result.Checks.ShouldAllBe(c => !string.IsNullOrWhiteSpace(c.Code));
    }

    [Fact]
    public void Evaluation_is_deterministic()
    {
        var request = TestFixtures.ValidRequest();

        var first = DeterministicRiskEvaluator.Evaluate(request);
        var second = DeterministicRiskEvaluator.Evaluate(request);

        second.Decision.ShouldBe(first.Decision);
        second.ApprovedQuantity.ShouldBe(first.ApprovedQuantity);
        second.Checks.Count.ShouldBe(first.Checks.Count);
    }
}
