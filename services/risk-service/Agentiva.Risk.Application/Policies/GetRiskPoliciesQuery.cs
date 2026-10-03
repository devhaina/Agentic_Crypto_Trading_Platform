using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.Risk.Application.Abstractions;

namespace Agentiva.Risk.Application.Policies;

/// <summary>Lists the configured risk policies.</summary>
public sealed record GetRiskPoliciesQuery : IQuery<Result<IReadOnlyList<RiskPolicyDto>>>;

/// <summary>Returns the active default risk policy.</summary>
public sealed record GetDefaultRiskPolicyQuery : IQuery<Result<RiskPolicyDto>>;

/// <summary>A risk policy, as exposed over the API.</summary>
/// <param name="Id">Policy identity.</param>
/// <param name="Name">Policy name.</param>
/// <param name="IsDefault">Whether this is the default policy.</param>
/// <param name="IsActive">Whether the policy may be applied.</param>
/// <param name="MaxRiskPerTradePercent">Maximum equity at risk per trade, in percent.</param>
/// <param name="MaxPositionNotional">Maximum value of one position, in the quote asset.</param>
/// <param name="MaxDailyLossPercent">Maximum loss in one UTC day, in percent of equity.</param>
/// <param name="MaxPortfolioExposurePercent">Maximum combined exposure, in percent of equity.</param>
/// <param name="MaxAssetConcentrationPercent">Maximum single-symbol exposure, in percent of equity.</param>
/// <param name="MaxOpenPositions">Maximum simultaneously open positions.</param>
/// <param name="MinConfidencePercent">Minimum signal confidence, in percent.</param>
/// <param name="MaxVolatilityPercent">Maximum acceptable symbol volatility, in percent.</param>
/// <param name="RequireStopLoss">Whether a stop-loss is mandatory.</param>
/// <param name="RequireTakeProfit">Whether a take-profit is mandatory.</param>
/// <param name="SlippageAssumptionPercent">Adverse slippage assumed per leg, in percent.</param>
/// <param name="TakerFeePercent">Taker fee assumed per leg, in percent.</param>
/// <param name="MarketDataStalenessThresholdSeconds">Maximum tolerated market data age.</param>
/// <param name="UpdatedAt">When the policy was last changed.</param>
/// <param name="UpdatedBy">Who last changed it.</param>
public sealed record RiskPolicyDto(
    Guid Id,
    string Name,
    bool IsDefault,
    bool IsActive,
    decimal MaxRiskPerTradePercent,
    decimal MaxPositionNotional,
    decimal MaxDailyLossPercent,
    decimal MaxPortfolioExposurePercent,
    decimal MaxAssetConcentrationPercent,
    int MaxOpenPositions,
    decimal MinConfidencePercent,
    decimal MaxVolatilityPercent,
    bool RequireStopLoss,
    bool RequireTakeProfit,
    decimal SlippageAssumptionPercent,
    decimal TakerFeePercent,
    double MarketDataStalenessThresholdSeconds,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

/// <summary>Maps a policy aggregate onto its API representation.</summary>
public static class RiskPolicyMapper
{
    public static RiskPolicyDto ToDto(Domain.Policies.RiskPolicy policy)
        => new(
            policy.Id.Value,
            policy.Name,
            policy.IsDefault,
            policy.IsActive,
            policy.MaxRiskPerTrade.Percent,
            policy.MaxPositionNotional.Amount,
            policy.MaxDailyLoss.Percent,
            policy.MaxPortfolioExposure.Percent,
            policy.MaxAssetConcentration.Percent,
            policy.MaxOpenPositions,
            policy.MinConfidence.Percent,
            policy.MaxVolatility.Percent,
            policy.RequireStopLoss,
            policy.RequireTakeProfit,
            policy.SlippageAssumption.Percent,
            policy.TakerFee.Percent,
            policy.MarketDataStalenessThreshold.TotalSeconds,
            policy.UpdatedAt,
            policy.UpdatedBy);
}

/// <summary>Handles <see cref="GetRiskPoliciesQuery"/>.</summary>
public sealed class GetRiskPoliciesQueryHandler(IRiskPolicyRepository policies)
    : IRequestHandler<GetRiskPoliciesQuery, Result<IReadOnlyList<RiskPolicyDto>>>
{
    public async Task<Result<IReadOnlyList<RiskPolicyDto>>> HandleAsync(
        GetRiskPoliciesQuery request,
        CancellationToken cancellationToken)
    {
        var all = await policies.ListAsync(cancellationToken);

        IReadOnlyList<RiskPolicyDto> dtos = all.Select(RiskPolicyMapper.ToDto).ToArray();
        return Result.Success(dtos);
    }
}

/// <summary>Handles <see cref="GetDefaultRiskPolicyQuery"/>.</summary>
public sealed class GetDefaultRiskPolicyQueryHandler(IRiskPolicyRepository policies)
    : IRequestHandler<GetDefaultRiskPolicyQuery, Result<RiskPolicyDto>>
{
    public async Task<Result<RiskPolicyDto>> HandleAsync(
        GetDefaultRiskPolicyQuery request,
        CancellationToken cancellationToken)
    {
        var policy = await policies.GetDefaultAsync(cancellationToken);

        return policy is null
            ? Result.Failure<RiskPolicyDto>(Error.NotFound(
                "risk.policy_unavailable", "No active default risk policy is configured."))
            : Result.Success(RiskPolicyMapper.ToDto(policy));
    }
}
