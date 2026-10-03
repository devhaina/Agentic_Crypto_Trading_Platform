namespace Agentiva.BuildingBlocks.Application.Mediator;

/// <summary>A request dispatched through the mediator, yielding <typeparamref name="TResponse"/>.</summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequest<out TResponse>;

/// <summary>
/// A state-changing request. Separated from <see cref="IQuery{TResponse}"/> so that
/// pipeline behaviors can treat the two differently — only commands get a database
/// transaction, an idempotency guard and an audit record.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface ICommand<out TResponse> : IRequest<TResponse>;

/// <summary>A read-only request. Must not mutate state.</summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IQuery<out TResponse> : IRequest<TResponse>;

/// <summary>
/// A command that moves money or creates an exchange order, and therefore must
/// carry an idempotency key.
/// </summary>
/// <remarks>
/// Implementing this interface opts the command into the idempotency behavior.
/// Any command that can cause a financial transaction must implement it; an
/// architecture test asserts that commands in the trading and execution
/// services do.
/// </remarks>
public interface IFinancialCommand<out TResponse> : ICommand<TResponse>
{
    /// <summary>
    /// Caller-supplied key that uniquely identifies this business operation.
    /// Replaying the same key must produce the original outcome, not a second order.
    /// </summary>
    string IdempotencyKey { get; }
}

/// <summary>Handles a single request type.</summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Continuation that invokes the next stage of the pipeline.</summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken);

/// <summary>
/// Cross-cutting stage wrapped around a handler — validation, logging,
/// transactions, idempotency. Executed in registration order.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}

/// <summary>An in-process notification, delivered to every registered handler.</summary>
public interface INotification;

/// <summary>Handles an in-process notification.</summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    Task HandleAsync(TNotification notification, CancellationToken cancellationToken);
}

/// <summary>Dispatches requests to their handler through the behavior pipeline.</summary>
public interface ISender
{
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

/// <summary>Publishes notifications to all registered handlers.</summary>
public interface IPublisher
{
    Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
