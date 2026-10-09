using Agentiva.BuildingBlocks.Application.Abstractions;
using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Risk.Application.Abstractions;
using Agentiva.Risk.Domain.Policies;
using FluentValidation;

namespace Agentiva.Risk.Application.Policies;

/// <summary>Creates a new risk policy. Not applied anywhere until promoted with <see cref="MarkRiskPolicyAsDefaultCommand"/>.</summary>
public sealed record CreateRiskPolicyCommand(
    string Name,
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
    string CreatedBy)
    : ICommand<Result<RiskPolicyDto>>;

/// <summary>Structural validation for a new risk policy.</summary>
/// <remarks>
/// Shape and range only. The cross-field invariants (risk-per-trade against
/// daily loss, concentration against exposure) are enforced once, in
/// <see cref="RiskPolicy.Create"/> itself, so there is exactly one place that
/// can diverge from the database's own constraint if the rule ever changes.
/// </remarks>
public sealed class CreateRiskPolicyCommandValidator : AbstractValidator<CreateRiskPolicyCommand>
{
    public CreateRiskPolicyCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
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
        RuleFor(c => c.CreatedBy).NotEmpty().MaximumLength(100);
    }
}

/// <summary>Handles <see cref="CreateRiskPolicyCommand"/>.</summary>
public sealed class CreateRiskPolicyCommandHandler(
    IRiskPolicyRepository policies, IUnitOfWork unitOfWork, IClock clock)
    : IRequestHandler<CreateRiskPolicyCommand, Result<RiskPolicyDto>>
{
    public async Task<Result<RiskPolicyDto>> HandleAsync(
        CreateRiskPolicyCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var quoteAsset = AssetCode.Create(command.QuoteAsset);

        var policy = RiskPolicy.Create(
            command.Name,
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
            now,
            command.CreatedBy);

        policies.Add(policy);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(RiskPolicyMapper.ToDto(policy));
    }
}
