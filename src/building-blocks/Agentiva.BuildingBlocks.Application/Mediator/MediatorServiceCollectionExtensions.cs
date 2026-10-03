using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Agentiva.BuildingBlocks.Application.Mediator;

/// <summary>Registration helpers for the in-process mediator.</summary>
public static class MediatorServiceCollectionExtensions
{
    /// <summary>
    /// Registers the mediator and scans the given assemblies for request and
    /// notification handlers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">Assemblies to scan. Typically the service's Application assembly.</param>
    public static IServiceCollection AddAgentivaMediator(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        services.AddScoped<Mediator>();
        services.AddScoped<ISender>(sp => sp.GetRequiredService<Mediator>());
        services.AddScoped<IPublisher>(sp => sp.GetRequiredService<Mediator>());

        foreach (var assembly in assemblies)
        {
            RegisterImplementationsOf(services, assembly, typeof(IRequestHandler<,>));
            RegisterImplementationsOf(services, assembly, typeof(INotificationHandler<>));
        }

        return services;
    }

    /// <summary>
    /// Registers an open generic pipeline behavior that applies to every request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Takes a <see cref="Type"/> rather than a type parameter because the
    /// behaviors that matter here are open generics — <c>ValidationBehavior&lt;,&gt;</c>,
    /// <c>LoggingBehavior&lt;,&gt;</c> — and C# has no syntax for passing an
    /// unbound generic as a type argument.
    /// </para>
    /// <para>
    /// Order matters: the first behavior registered is the outermost, so it
    /// observes everything inside it. Register validation before idempotency so
    /// that a malformed request is rejected before it consumes a key, and
    /// logging before both so that a rejection is still logged.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="openGenericBehaviorType">
    /// An open generic type definition with two type parameters implementing
    /// <see cref="IPipelineBehavior{TRequest,TResponse}"/>, e.g.
    /// <c>typeof(ValidationBehavior&lt;,&gt;)</c>.
    /// </param>
    /// <exception cref="ArgumentException">The type is not a suitable open generic.</exception>
    public static IServiceCollection AddPipelineBehavior(
        this IServiceCollection services,
        Type openGenericBehaviorType)
    {
        ArgumentNullException.ThrowIfNull(openGenericBehaviorType);

        // Caught here rather than as an opaque DI container error at the first
        // BuildServiceProvider call, which is a long way from the mistake.
        if (!openGenericBehaviorType.IsGenericTypeDefinition
            || openGenericBehaviorType.GetGenericArguments().Length != 2)
        {
            throw new ArgumentException(
                $"{openGenericBehaviorType.Name} must be an open generic type definition with two type "
                + "parameters, for example typeof(ValidationBehavior<,>). To register a behavior for one "
                + "specific request type, use the generic AddPipelineBehavior<TRequest, TResponse, TBehavior> overload.",
                nameof(openGenericBehaviorType));
        }

        services.AddScoped(typeof(IPipelineBehavior<,>), openGenericBehaviorType);
        return services;
    }

    /// <summary>
    /// Registers a pipeline behavior for one specific request and response pair.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <typeparam name="TBehavior">The closed behavior implementation.</typeparam>
    public static IServiceCollection AddPipelineBehavior<TRequest, TResponse, TBehavior>(
        this IServiceCollection services)
        where TRequest : IRequest<TResponse>
        where TBehavior : class, IPipelineBehavior<TRequest, TResponse>
    {
        services.AddScoped<IPipelineBehavior<TRequest, TResponse>, TBehavior>();
        return services;
    }

    private static void RegisterImplementationsOf(
        IServiceCollection services,
        Assembly assembly,
        Type openGenericInterface)
    {
        var candidates = assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });

        foreach (var type in candidates)
        {
            var closedInterfaces = type
                .GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGenericInterface);

            foreach (var closedInterface in closedInterfaces)
            {
                services.AddScoped(closedInterface, type);
            }
        }
    }
}
