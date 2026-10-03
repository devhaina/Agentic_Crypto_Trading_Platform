using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Agentiva.BuildingBlocks.Application.Mediator;

/// <summary>
/// In-process mediator implementing <see cref="ISender"/> and <see cref="IPublisher"/>.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written rather than taken from a library. MediatR — the usual choice — moved
/// to a paid commercial licence at version 13, and the engineering requirement
/// explicitly permits "MediatR or equivalent". Vendoring roughly a hundred lines
/// removes a licence obligation from a financial product and keeps the dispatch
/// path small enough to reason about.
/// </para>
/// <para>
/// Dispatch is by the request's <em>runtime</em> type, so a handler registered for
/// a concrete command is found even when the caller holds an interface reference.
/// The closed generic wrapper per request type is built once by reflection and
/// cached, so steady-state dispatch costs a dictionary lookup and a virtual call.
/// </para>
/// </remarks>
public sealed class Mediator(IServiceProvider serviceProvider) : ISender, IPublisher
{
    private static readonly ConcurrentDictionary<Type, object> RequestWrappers = new();
    private static readonly ConcurrentDictionary<Type, Type> NotificationHandlerTypes = new();

    public Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wrapper = (RequestWrapper<TResponse>)RequestWrappers.GetOrAdd(
            request.GetType(),
            static (requestType, responseType) =>
            {
                var wrapperType = typeof(RequestWrapperImpl<,>).MakeGenericType(requestType, responseType);
                return Activator.CreateInstance(wrapperType)
                       ?? throw new InvalidOperationException($"Could not create a dispatcher for {requestType}.");
            },
            typeof(TResponse));

        return wrapper.HandleAsync(request, serviceProvider, cancellationToken);
    }

    public async Task PublishAsync<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        var handlerType = NotificationHandlerTypes.GetOrAdd(
            typeof(TNotification),
            static t => typeof(INotificationHandler<>).MakeGenericType(t));

        var handlers = serviceProvider.GetServices(handlerType);

        // Sequential, not concurrent. Notification handlers here run inside the
        // caller's scope and frequently touch the same DbContext, which is not
        // thread-safe. Fan-out concurrency belongs on the RabbitMQ bus instead.
        foreach (var handler in handlers)
        {
            if (handler is INotificationHandler<TNotification> typed)
            {
                await typed.HandleAsync(notification, cancellationToken);
            }
        }
    }

    private abstract class RequestWrapper<TResponse>
    {
        public abstract Task<TResponse> HandleAsync(
            IRequest<TResponse> request,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken);
    }

    private sealed class RequestWrapperImpl<TRequest, TResponse> : RequestWrapper<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> HandleAsync(
            IRequest<TResponse> request,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken)
        {
            var typedRequest = (TRequest)request;

            var handler = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>()
                          ?? throw new InvalidOperationException(
                              $"No handler registered for {typeof(TRequest).Name}. "
                              + "Call AddAgentivaMediator with the assembly containing it.");

            RequestHandlerDelegate<TResponse> pipeline =
                ct => handler.HandleAsync(typedRequest, ct);

            // Wrap in reverse registration order so that the first-registered
            // behavior ends up outermost and therefore runs first.
            var behaviors = serviceProvider
                .GetServices<IPipelineBehavior<TRequest, TResponse>>()
                .Reverse();

            foreach (var behavior in behaviors)
            {
                var next = pipeline;
                pipeline = ct => behavior.HandleAsync(typedRequest, next, ct);
            }

            return pipeline(cancellationToken);
        }
    }
}
