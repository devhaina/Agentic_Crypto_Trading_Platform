using Agentiva.BuildingBlocks.Application.Exceptions;
using Agentiva.BuildingBlocks.Application.Mediator;
using FluentValidation;

namespace Agentiva.BuildingBlocks.Application.Behaviors;

/// <summary>
/// Runs every registered FluentValidation validator for a request before the
/// handler sees it.
/// </summary>
/// <remarks>
/// Placed outermost-but-one in the pipeline, ahead of any transaction or
/// idempotency stage. A malformed request must be rejected before it consumes an
/// idempotency key or opens a database transaction.
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var applicable = validators as IValidator<TRequest>[] ?? validators.ToArray();

        if (applicable.Length == 0)
        {
            return await next(cancellationToken);
        }

        var context = new ValidationContext<TRequest>(request);

        var results = await Task.WhenAll(
            applicable.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToArray();

        if (failures.Length == 0)
        {
            return await next(cancellationToken);
        }

        var errors = failures
            .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(f => f.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        throw new AgentivaValidationException(errors);
    }
}
