using Agentiva.BuildingBlocks.Messaging.Abstractions;
using Agentiva.BuildingBlocks.Messaging.Configuration;
using Agentiva.Contracts.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agentiva.BuildingBlocks.Messaging.RabbitMq;

/// <summary>Registration helpers for the RabbitMQ event bus.</summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the connection, publisher and consumer for this service.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration root, read from the <c>RabbitMq</c> section.</param>
    /// <param name="serviceName">
    /// This service's name, used for its queue names and its broker connection label.
    /// </param>
    public static IServiceCollection AddAgentivaMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        services
            .AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Configure(o => o.ServiceName = serviceName)
            .ValidateDataAnnotations()

            // Fail at startup rather than on the first publish. A service that
            // boots healthy and only then discovers it cannot reach the broker
            // reports itself as ready while being unable to do its job.
            .ValidateOnStart();

        services.AddSingleton<IRabbitMqConnectionProvider, RabbitMqConnectionProvider>();
        services.AddSingleton<IRabbitMqTopologyInitializer, RabbitMqTopologyInitializer>();
        services.AddScoped<IEventPublisher, RabbitMqEventPublisher>();

        return services;
    }

    /// <summary>
    /// Subscribes a handler to an event, registering the handler and its binding.
    /// </summary>
    /// <typeparam name="TEvent">The event to consume.</typeparam>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="routingKey">Routing key from <c>EventTypes</c>.</param>
    public static IServiceCollection Subscribe<TEvent, THandler>(
        this IServiceCollection services,
        string routingKey)
        where TEvent : IntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.AddScoped<THandler>();
        services.AddSingleton(new SubscriptionDescriptor(routingKey, typeof(THandler)));
        return services;
    }

    /// <summary>
    /// Starts the background consumer. Call once, after every
    /// <see cref="Subscribe{TEvent,THandler}"/> registration.
    /// </summary>
    public static IServiceCollection AddAgentivaEventConsumer(this IServiceCollection services)
    {
        services.AddHostedService<RabbitMqConsumerService>();
        return services;
    }
}
