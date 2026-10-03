using Agentiva.BuildingBlocks.Domain.Primitives;
using FluentValidation;

namespace Agentiva.Risk.Application.Evaluations;

/// <summary>Validates the shape of an evaluation request.</summary>
/// <remarks>
/// Deliberately limited to structural validation — is the symbol well formed, is
/// the price positive, is confidence in range. Every <em>risk</em> decision
/// belongs in the deterministic evaluator, not here: a limit enforced in a
/// validator would be invisible to the audit record and could not be explained
/// after the fact.
/// </remarks>
public sealed class EvaluateIntentCommandValidator : AbstractValidator<EvaluateIntentCommand>
{
    public EvaluateIntentCommandValidator()
    {
        RuleFor(c => c.IdempotencyKey)
            .NotEmpty().WithMessage("An idempotency key is required for a risk evaluation.")
            .MaximumLength(200);

        RuleFor(c => c.TradingIntentId)
            .NotEmpty().WithMessage("A trading intent id is required.");

        RuleFor(c => c.Symbol)
            .NotEmpty()
            .Must(s => Symbol.TryCreate(s, out _))
            .WithMessage("Symbol must be an alphanumeric exchange symbol such as BTCUSDT.");

        RuleFor(c => c.Side)
            .NotEmpty()
            .Must(side => string.Equals(side, "BUY", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(side, "SELL", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Side must be BUY or SELL.");

        RuleFor(c => c.EntryPrice)
            .GreaterThan(0m).WithMessage("Entry price must be greater than zero.");

        RuleFor(c => c.StopLoss)
            .GreaterThan(0m).When(c => c.StopLoss.HasValue)
            .WithMessage("Stop-loss must be greater than zero when supplied.");

        RuleFor(c => c.TakeProfit)
            .GreaterThan(0m).When(c => c.TakeProfit.HasValue)
            .WithMessage("Take-profit must be greater than zero when supplied.");

        // Confidence arrives as a 0-1 fraction, matching the agent and signal
        // contracts. It is converted to a Percentage in the handler.
        RuleFor(c => c.Confidence)
            .InclusiveBetween(0m, 1m)
            .WithMessage("Confidence must be a fraction between 0 and 1 inclusive.");

        RuleFor(c => c.SymbolVolatilityPercent)
            .InclusiveBetween(0m, 100m)
            .WithMessage("Symbol volatility must be a percentage between 0 and 100.");

        RuleFor(c => c.MarketDataAgeSeconds)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Market data age cannot be negative.");

        RuleFor(c => c.Portfolio).NotNull();

        When(c => c.Portfolio is not null, () =>
        {
            RuleFor(c => c.Portfolio.Equity)
                .GreaterThanOrEqualTo(0m).WithMessage("Equity cannot be negative.");

            RuleFor(c => c.Portfolio.AvailableBalance)
                .GreaterThanOrEqualTo(0m).WithMessage("Available balance cannot be negative.");

            RuleFor(c => c.Portfolio.CurrentExposure)
                .GreaterThanOrEqualTo(0m).WithMessage("Exposure cannot be negative.");

            RuleFor(c => c.Portfolio.CurrentSymbolExposure)
                .GreaterThanOrEqualTo(0m).WithMessage("Symbol exposure cannot be negative.");

            RuleFor(c => c.Portfolio.OpenPositionCount)
                .GreaterThanOrEqualTo(0).WithMessage("Open position count cannot be negative.");

            // DailyPnl is intentionally unconstrained in sign: a loss is a
            // legitimate negative value.
        });
    }
}
