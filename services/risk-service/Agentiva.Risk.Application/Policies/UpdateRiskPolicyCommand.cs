using Agentiva.BuildingBlocks.Application.Abstractions;
using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Risk.Application.Abstractions;
using FluentValidation;

namespace Agentiva.Risk.Application.Policies;

/// <summary>Replaces every limit on an existing risk policy.</summary>
public sealed record UpdateRiskPolicyCommand(
    Guid RiskPolicyId,
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
    string QuoteAsset,
    string UpdatedBy)
    : ICommand<Result<RiskPolicyDto>>;

/// <summary>Structural validation, mirroring <see cref="CreateRiskPolicyCommandValidator"/>.</summary>
public sealed class UpdateRiskPolicyCommandValidator : AbstractValidator<UpdateRiskPolicyCommand>
{
    public UpdateRiskPolicyCommandValidator()
    {
        RuleFor(c => c.RiskPolicyId).NotEmpty();
        RuleFor(c => c.MaxRiskPerTradePercent).InclusiveBetween(0m, 100m);
        RuleFor(c => c.MaxPositionNotional).GreaterThan(0m);
        RuleFor(c => c.MaxDailyLossPercent).InclusiveBetween(0m, 100m);
        RuleFor(c => c.MaxPortfolioExposurePercent).InclusiveBetween(0m, 100m);
        RuleFor(c => c.MaxAssetConcentrationPercent).InclusiveBetween(0m, 100m);
        RuleFor(c => c.MaxOpenPositions).InclusiveBetween(1, 100);
        RuleFor(c => c.MinConfidencePercent).InclusiveBetween(0m, 100m);
        RuleFor(c => c.MaxVolatilityPercent).InclusiveBetween(0m, 100m);
        RuleFor(c => c.SlippageAssumptionPercent).InclusiveBetween(0m, 100m);
        RuleFor(c => c.TakerFeePercent).InclusiveBetween(0m, 100m);
        RuleFor(c => c.MarketDataStalenessThresholdSeconds).InclusiveBetween(1, 600);
        RuleFor(c => c.QuoteAsset).NotEmpty().MaximumLength(12);
        RuleFor(c => c.UpdatedBy).NotEmpty().MaximumLength(100);
    }
}

/// <summary>Handles <see cref="UpdateRiskPolicyCommand"/>.</summary>
public sealed class UpdateRiskPolicyCommandHandler(
    IRiskPolicyRepository policies, IUnitOfWork unitOfWork, IClock clock)
    : IRequestHandler<UpdateRiskPolicyCommand, Result<RiskPolicyDto>>
{
    public async Task<Result<RiskPolicyDto>> HandleAsync(
        UpdateRiskPolicyCommand command, CancellationToken cancellationToken)
    {
        var policy = await policies.GetByIdAsync(RiskPolicyId.From(command.RiskPolicyId), cancellationToken);

        if (policy is null)
        {
            return Result.Failure<RiskPolicyDto>(Error.NotFound(
                "risk.policy_not_found", $"No risk policy with id {command.RiskPolicyId} exists."));
        }

        var quoteAsset = AssetCode.Create(command.QuoteAsset);

        policy.UpdateLimits(
            Percentage.FromPercent(command.MaxRiskPerTradePercent),
            Money.Create(command.MaxPositionNotional, quoteAsset),
            Percentage.FromPercent(command.MaxDailyLossPercent),
            Percentage.FromPercent(command.MaxPortfolioExposurePercent),
            Percentage.FromPercent(command.MaxAssetConcentrationPercent),
            command.MaxOpenPositions,
            Percentage.FromPercent(command.MinConfidencePercent),
            Percentage.FromPercent(command.MaxVolatilityPercent),
            command.RequireStopLoss,
            command.RequireTakeProfit,
            Percentage.FromPercent(command.SlippageAssumptionPercent),
            Percentage.FromPercent(command.TakerFeePercent),
            TimeSpan.FromSeconds(command.MarketDataStalenessThresholdSeconds),
            clock.UtcNow,
            command.UpdatedBy);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(RiskPolicyMapper.ToDto(policy));
    }
}
