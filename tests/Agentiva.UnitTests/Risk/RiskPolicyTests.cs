using Agentiva.BuildingBlocks.Domain.Exceptions;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Risk.Domain.Policies;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.Risk;

/// <summary>Tests for the Phase 4 risk-policy CRUD operations on <see cref="RiskPolicy"/>.</summary>
public sealed class RiskPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void UpdateLimits_replaces_every_field()
    {
        var policy = TestFixtures.DefaultPolicy();

        policy.UpdateLimits(
            maxRiskPerTrade: Percentage.FromPercent(1m),
            maxPositionNotional: Money.Create(2_000m, TestFixtures.Usdt),
            maxDailyLoss: Percentage.FromPercent(3m),
            maxPortfolioExposure: Percentage.FromPercent(60m),
            maxAssetConcentration: Percentage.FromPercent(30m),
            maxOpenPositions: 10,
            minConfidence: Percentage.FromPercent(70m),
            maxVolatility: Percentage.FromPercent(80m),
            requireStopLoss: false,
            requireTakeProfit: false,
            slippageAssumption: Percentage.FromPercent(0.1m),
            takerFee: Percentage.FromPercent(0.2m),
            marketDataStalenessThreshold: TimeSpan.FromSeconds(60),
            now: Now,
            updatedBy: "operator-1");

        policy.MaxRiskPerTrade.Percent.ShouldBe(1m);
        policy.MaxPositionNotional.Amount.ShouldBe(2_000m);
        policy.MaxDailyLoss.Percent.ShouldBe(3m);
        policy.MaxPortfolioExposure.Percent.ShouldBe(60m);
        policy.MaxAssetConcentration.Percent.ShouldBe(30m);
        policy.MaxOpenPositions.ShouldBe(10);
        policy.RequireStopLoss.ShouldBeFalse();
        policy.RequireTakeProfit.ShouldBeFalse();
        policy.MarketDataStalenessThreshold.ShouldBe(TimeSpan.FromSeconds(60));
        policy.UpdatedBy.ShouldBe("operator-1");
        policy.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void UpdateLimits_rejects_risk_per_trade_above_daily_loss()
    {
        var policy = TestFixtures.DefaultPolicy();

        Should.Throw<DomainException>(() => policy.UpdateLimits(
                maxRiskPerTrade: Percentage.FromPercent(5m),
                maxPositionNotional: Money.Create(1_000m, TestFixtures.Usdt),
                maxDailyLoss: Percentage.FromPercent(2m),
                maxPortfolioExposure: Percentage.FromPercent(50m),
                maxAssetConcentration: Percentage.FromPercent(25m),
                maxOpenPositions: 5,
                minConfidence: Percentage.FromPercent(60m),
                maxVolatility: Percentage.FromPercent(100m),
                requireStopLoss: true,
                requireTakeProfit: true,
                slippageAssumption: Percentage.FromPercent(0.05m),
                takerFee: Percentage.FromPercent(0.1m),
                marketDataStalenessThreshold: TimeSpan.FromSeconds(30),
                now: Now,
                updatedBy: "operator-1"))
            .Code.ShouldBe("risk.policy.inconsistent_risk_limits");
    }

    [Fact]
    public void UpdateLimits_rejects_concentration_above_exposure()
    {
        var policy = TestFixtures.DefaultPolicy();

        Should.Throw<DomainException>(() => policy.UpdateLimits(
                maxRiskPerTrade: Percentage.FromPercent(0.5m),
                maxPositionNotional: Money.Create(1_000m, TestFixtures.Usdt),
                maxDailyLoss: Percentage.FromPercent(2m),
                maxPortfolioExposure: Percentage.FromPercent(20m),
                maxAssetConcentration: Percentage.FromPercent(25m),
                maxOpenPositions: 5,
                minConfidence: Percentage.FromPercent(60m),
                maxVolatility: Percentage.FromPercent(100m),
                requireStopLoss: true,
                requireTakeProfit: true,
                slippageAssumption: Percentage.FromPercent(0.05m),
                takerFee: Percentage.FromPercent(0.1m),
                marketDataStalenessThreshold: TimeSpan.FromSeconds(30),
                now: Now,
                updatedBy: "operator-1"))
            .Code.ShouldBe("risk.policy.inconsistent_exposure_limits");
    }

    [Fact]
    public void Deactivate_refuses_to_deactivate_the_default()
    {
        var policy = TestFixtures.DefaultPolicy();

        Should.Throw<DomainException>(() => policy.Deactivate(Now, "operator-1"))
            .Code.ShouldBe("risk.policy.cannot_deactivate_default");
    }

    [Fact]
    public void DemoteFromDefault_clears_the_flag_without_deactivating()
    {
        var policy = TestFixtures.DefaultPolicy();

        policy.DemoteFromDefault(Now, "operator-1");

        policy.IsDefault.ShouldBeFalse();
        policy.IsActive.ShouldBeTrue();

        // No longer the default, so it can now be deactivated.
        policy.Deactivate(Now, "operator-1");
        policy.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void MarkAsDefault_sets_the_flag_without_touching_other_policies()
    {
        var policy = RiskPolicy.Create(
            name: "secondary",
            maxRiskPerTrade: Percentage.FromPercent(0.5m),
            maxPositionNotional: Money.Create(1_000m, TestFixtures.Usdt),
            maxDailyLoss: Percentage.FromPercent(2m),
            maxPortfolioExposure: Percentage.FromPercent(50m),
            maxAssetConcentration: Percentage.FromPercent(25m),
            maxOpenPositions: 5,
            minConfidence: Percentage.FromPercent(60m),
            maxVolatility: Percentage.FromPercent(100m),
            requireStopLoss: true,
            requireTakeProfit: true,
            slippageAssumption: Percentage.FromPercent(0.05m),
            takerFee: Percentage.FromPercent(0.1m),
            marketDataStalenessThreshold: TimeSpan.FromSeconds(30),
            now: Now,
            updatedBy: "operator-1");

        policy.IsDefault.ShouldBeFalse();

        policy.MarkAsDefault(Now, "operator-2");

        policy.IsDefault.ShouldBeTrue();
        policy.UpdatedBy.ShouldBe("operator-2");
    }
}
