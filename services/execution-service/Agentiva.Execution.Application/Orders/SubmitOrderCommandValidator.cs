using Agentiva.BuildingBlocks.Domain.Primitives;
using FluentValidation;

namespace Agentiva.Execution.Application.Orders;

/// <summary>Structural validation for a new order. Every exchange and risk rule lives elsewhere.</summary>
public sealed class SubmitOrderCommandValidator : AbstractValidator<SubmitOrderCommand>
{
    private static readonly string[] ValidSides = ["BUY", "SELL"];
    private static readonly string[] ValidOrderTypes = ["MARKET", "LIMIT"];

    public SubmitOrderCommandValidator()
    {
        RuleFor(c => c.IdempotencyKey)
            .NotEmpty().WithMessage("An idempotency key is required to submit an order.")
            .MaximumLength(200);

        RuleFor(c => c.TradingIntentId).NotEmpty();
        RuleFor(c => c.RiskCheckId).NotEmpty();
        RuleFor(c => c.TradingAccountId).NotEmpty();

        RuleFor(c => c.Symbol)
            .NotEmpty()
            .Must(s => Symbol.TryCreate(s, out _))
            .WithMessage("Symbol must be an alphanumeric exchange symbol such as BTCUSDT.");

        RuleFor(c => c.Side)
            .NotEmpty()
            .Must(side => ValidSides.Contains(side, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Side must be one of: {string.Join(", ", ValidSides)}.");

        RuleFor(c => c.OrderType)
            .NotEmpty()
            .Must(type => ValidOrderTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Order type must be one of: {string.Join(", ", ValidOrderTypes)}.");

        RuleFor(c => c.Quantity)
            .GreaterThan(0m).WithMessage("Quantity must be greater than zero.");

        RuleFor(c => c.LimitPrice)
            .NotNull()
            .WithMessage("A limit order must carry a limit price.")
            .When(c => string.Equals(c.OrderType, "LIMIT", StringComparison.OrdinalIgnoreCase));

        RuleFor(c => c.LimitPrice)
            .GreaterThan(0m).When(c => c.LimitPrice.HasValue)
            .WithMessage("Limit price must be greater than zero when supplied.");

        RuleFor(c => c.ReferencePrice)
            .GreaterThan(0m).WithMessage("A reference price greater than zero is required to mark a simulated fill.");
    }
}
