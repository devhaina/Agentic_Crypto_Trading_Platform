using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.Trading.Application.Abstractions;
using FluentValidation;

namespace Agentiva.Trading.Application.Ledger;

/// <summary>Returns the current paper-trading ledger: the operator-set state if any, else the configured baseline.</summary>
public sealed record GetPaperLedgerQuery : IQuery<Result<PaperLedgerDto>>;

/// <summary>
/// Sets the operator-adjustable paper-trading ledger. <c>UpdatedBy</c> is the
/// operator user id, taken from the authenticated principal.
/// </summary>
public sealed record SetPaperLedgerCommand(
    decimal Equity,
    decimal AvailableBalance,
    decimal CurrentExposure,
    int OpenPositionCount,
    decimal DailyPnl,
    string UpdatedBy)
    : ICommand<Result<PaperLedgerDto>>;

/// <summary>Clears the operator-set ledger, reverting to the configured baseline.</summary>
public sealed record ResetPaperLedgerCommand(string UpdatedBy) : ICommand<Result<PaperLedgerDto>>;

public sealed class SetPaperLedgerCommandValidator : AbstractValidator<SetPaperLedgerCommand>
{
    public SetPaperLedgerCommandValidator()
    {
        RuleFor(c => c.Equity).GreaterThanOrEqualTo(0m);
        RuleFor(c => c.AvailableBalance).InclusiveBetween(0m, decimal.MaxValue);
        RuleFor(c => c.AvailableBalance)
            .LessThanOrEqualTo(c => c.Equity)
            .WithMessage("Available balance cannot exceed total equity.");
        RuleFor(c => c.CurrentExposure).GreaterThanOrEqualTo(0m);
        RuleFor(c => c.OpenPositionCount).GreaterThanOrEqualTo(0);
        RuleFor(c => c.UpdatedBy).NotEmpty().MaximumLength(100);
    }
}

/// <summary>Handles <see cref="GetPaperLedgerQuery"/>.</summary>
public sealed class GetPaperLedgerQueryHandler(IPaperLedgerStore store, IPaperLedgerDefaults defaults)
    : IRequestHandler<GetPaperLedgerQuery, Result<PaperLedgerDto>>
{
    public async Task<Result<PaperLedgerDto>> HandleAsync(GetPaperLedgerQuery request, CancellationToken cancellationToken)
    {
        var ledger = await store.GetAsync(cancellationToken) ?? defaults.ConfiguredBaseline();
        return Result.Success(ledger);
    }
}

/// <summary>Handles <see cref="SetPaperLedgerCommand"/>.</summary>
public sealed class SetPaperLedgerCommandHandler(IPaperLedgerStore store)
    : IRequestHandler<SetPaperLedgerCommand, Result<PaperLedgerDto>>
{
    public async Task<Result<PaperLedgerDto>> HandleAsync(SetPaperLedgerCommand command, CancellationToken cancellationToken)
    {
        await store.SetAsync(
            command.Equity, command.AvailableBalance, command.CurrentExposure, command.OpenPositionCount,
            command.DailyPnl, command.UpdatedBy, cancellationToken);

        var ledger = await store.GetAsync(cancellationToken);
        return Result.Success(ledger!);
    }
}

/// <summary>Handles <see cref="ResetPaperLedgerCommand"/>.</summary>
public sealed class ResetPaperLedgerCommandHandler(IPaperLedgerStore store, IPaperLedgerDefaults defaults)
    : IRequestHandler<ResetPaperLedgerCommand, Result<PaperLedgerDto>>
{
    public async Task<Result<PaperLedgerDto>> HandleAsync(ResetPaperLedgerCommand command, CancellationToken cancellationToken)
    {
        await store.ResetAsync(cancellationToken);
        return Result.Success(defaults.ConfiguredBaseline());
    }
}
