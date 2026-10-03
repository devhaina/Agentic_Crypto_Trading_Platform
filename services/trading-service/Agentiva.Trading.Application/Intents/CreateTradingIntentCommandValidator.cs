using Agentiva.BuildingBlocks.Domain.Primitives;
using FluentValidation;

namespace Agentiva.Trading.Application.Intents;

/// <summary>Structural validation for a new trading intent.</summary>
/// <remarks>
/// Shape only. Every risk limit is enforced by the Risk Service so that each
/// decision is recorded, explainable and governed by one policy — a limit
/// enforced here would be invisible to the audit record.
/// </remarks>
public sealed class CreateTradingIntentCommandValidator : AbstractValidator<CreateTradingIntentCommand>
{
    private static readonly string[] ValidSources = ["STRATEGY", "AGENT", "MANUAL"];

    public CreateTradingIntentCommandValidator()
    {
        RuleFor(c => c.IdempotencyKey)
            .NotEmpty().WithMessage("An idempotency key is required to create a trading intent.")
            .MaximumLength(200);

        RuleFor(c => c.TradingAccountId)
            .NotEmpty().WithMessage("A trading account id is required.");

        RuleFor(c => c.Symbol)
            .NotEmpty()
            .Must(s => Symbol.TryCreate(s, out _))
            .WithMessage("Symbol must be an alphanumeric exchange symbol such as BTCUSDT.");

        RuleFor(c => c.Side)
            .NotEmpty()
            .Must(side => string.Equals(side, "BUY", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(side, "SELL", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Side must be BUY or SELL.");

        RuleFor(c => c.Quantity)
            .GreaterThan(0m).WithMessage("Requested quantity must be greater than zero.");

        RuleFor(c => c.EntryPrice)
            .GreaterThan(0m).When(c => c.EntryPrice.HasValue)
            .WithMessage("Entry price must be greater than zero when supplied.");

        RuleFor(c => c.StopLoss)
            .GreaterThan(0m).When(c => c.StopLoss.HasValue)
            .WithMessage("Stop-loss must be greater than zero when supplied.");

        RuleFor(c => c.TakeProfit)
            .GreaterThan(0m).When(c => c.TakeProfit.HasValue)
            .WithMessage("Take-profit must be greater than zero when supplied.");

        RuleFor(c => c.Confidence)
            .InclusiveBetween(0m, 1m)
            .WithMessage("Confidence must be a fraction between 0 and 1 inclusive.");

        RuleFor(c => c.Source)
            .NotEmpty()
            .Must(s => ValidSources.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Source must be one of: {string.Join(", ", ValidSources)}.");

        RuleFor(c => c.CreatedBy)
            .NotEmpty().MaximumLength(100);
    }
}
