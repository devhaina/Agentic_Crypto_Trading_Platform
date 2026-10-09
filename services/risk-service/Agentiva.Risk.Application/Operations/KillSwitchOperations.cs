using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.Risk.Application.Abstractions;
using FluentValidation;

namespace Agentiva.Risk.Application.Operations;

/// <summary>Current kill-switch and trading-admission state.</summary>
/// <param name="Engaged">Whether the kill switch is currently engaged.</param>
/// <param name="ForcedByConfiguration">
/// Whether configuration forces the switch engaged regardless of the operator
/// flag — see the remarks on <c>IPlatformStateProvider.IsKillSwitchForcedByConfiguration</c>.
/// </param>
/// <param name="TradingEnabled">Whether new order admission is enabled.</param>
/// <param name="EffectiveTradingMode">The platform's effective trading mode.</param>
public sealed record KillSwitchStatusDto(
    bool Engaged, bool ForcedByConfiguration, bool TradingEnabled, string EffectiveTradingMode);

/// <summary>Returns the current kill-switch and trading-admission state.</summary>
public sealed record GetKillSwitchStatusQuery : IQuery<Result<KillSwitchStatusDto>>;

/// <summary>
/// Engages the global kill switch. Every subsequent risk evaluation is
/// rejected until it is released.
/// </summary>
/// <param name="Trigger">Stable trigger code, e.g. <c>operator_manual</c> or <c>daily_loss_exceeded</c>.</param>
/// <param name="Detail">Human-readable context for the activation.</param>
/// <param name="ActivatedBy">Operator user id, taken from the authenticated principal.</param>
public sealed record EngageKillSwitchCommand(string Trigger, string Detail, string ActivatedBy)
    : ICommand<Result<KillSwitchStatusDto>>;

/// <summary>Releases the global kill switch.</summary>
/// <param name="DeactivatedBy">Operator user id, taken from the authenticated principal.</param>
/// <param name="Justification">Why it is now safe to resume trading. Required — never released silently.</param>
public sealed record ReleaseKillSwitchCommand(string DeactivatedBy, string Justification)
    : ICommand<Result<KillSwitchStatusDto>>;

public sealed class EngageKillSwitchCommandValidator : AbstractValidator<EngageKillSwitchCommand>
{
    public EngageKillSwitchCommandValidator()
    {
        RuleFor(c => c.Trigger).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Detail).NotEmpty().MaximumLength(2000);
        RuleFor(c => c.ActivatedBy).NotEmpty().MaximumLength(100);
    }
}

public sealed class ReleaseKillSwitchCommandValidator : AbstractValidator<ReleaseKillSwitchCommand>
{
    public ReleaseKillSwitchCommandValidator()
    {
        RuleFor(c => c.DeactivatedBy).NotEmpty().MaximumLength(100);

        RuleFor(c => c.Justification)
            .NotEmpty().WithMessage("A justification is required to release the kill switch.")
            .MaximumLength(2000);
    }
}

/// <summary>Handles <see cref="GetKillSwitchStatusQuery"/>.</summary>
public sealed class GetKillSwitchStatusQueryHandler(IPlatformStateProvider platformState)
    : IRequestHandler<GetKillSwitchStatusQuery, Result<KillSwitchStatusDto>>
{
    public async Task<Result<KillSwitchStatusDto>> HandleAsync(
        GetKillSwitchStatusQuery request, CancellationToken cancellationToken)
    {
        var engaged = await platformState.IsKillSwitchEngagedAsync(cancellationToken);
        var tradingEnabled = await platformState.IsTradingEnabledAsync(cancellationToken);

        return Result.Success(new KillSwitchStatusDto(
            engaged,
            platformState.IsKillSwitchForcedByConfiguration,
            tradingEnabled,
            platformState.EffectiveTradingMode.ToString()));
    }
}

/// <summary>Handles <see cref="EngageKillSwitchCommand"/>.</summary>
public sealed class EngageKillSwitchCommandHandler(IPlatformStateProvider platformState)
    : IRequestHandler<EngageKillSwitchCommand, Result<KillSwitchStatusDto>>
{
    public async Task<Result<KillSwitchStatusDto>> HandleAsync(
        EngageKillSwitchCommand command, CancellationToken cancellationToken)
    {
        await platformState.EngageKillSwitchAsync(
            command.Trigger, command.Detail, command.ActivatedBy, cancellationToken);

        return Result.Success(new KillSwitchStatusDto(
            Engaged: true,
            ForcedByConfiguration: platformState.IsKillSwitchForcedByConfiguration,
            TradingEnabled: await platformState.IsTradingEnabledAsync(cancellationToken),
            EffectiveTradingMode: platformState.EffectiveTradingMode.ToString()));
    }
}

/// <summary>Handles <see cref="ReleaseKillSwitchCommand"/>.</summary>
public sealed class ReleaseKillSwitchCommandHandler(IPlatformStateProvider platformState)
    : IRequestHandler<ReleaseKillSwitchCommand, Result<KillSwitchStatusDto>>
{
    public async Task<Result<KillSwitchStatusDto>> HandleAsync(
        ReleaseKillSwitchCommand command, CancellationToken cancellationToken)
    {
        await platformState.ReleaseKillSwitchAsync(
            command.DeactivatedBy, command.Justification, cancellationToken);

        // Re-read rather than assume: configuration may still force the
        // switch engaged, in which case the Redis flag was cleared but the
        // effective state has not changed, and the caller must see that.
        var engaged = await platformState.IsKillSwitchEngagedAsync(cancellationToken);

        return Result.Success(new KillSwitchStatusDto(
            engaged,
            platformState.IsKillSwitchForcedByConfiguration,
            await platformState.IsTradingEnabledAsync(cancellationToken),
            platformState.EffectiveTradingMode.ToString()));
    }
}
