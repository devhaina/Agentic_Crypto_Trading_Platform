using Agentiva.BuildingBlocks.Application.Behaviors;
using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Persistence;
using Agentiva.Execution.Application.Abstractions;
using Agentiva.Execution.Application.Orders;
using Agentiva.Execution.Infrastructure.Binance;
using Agentiva.Execution.Infrastructure.Configuration;
using Agentiva.Execution.Infrastructure.Persistence;
using Agentiva.Execution.Infrastructure.Providers;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Agentiva.Execution.Infrastructure;

/// <summary>Composition root for the Execution Service.</summary>
public static class ExecutionInfrastructureExtensions
{
    /// <summary>Registers the execution application and infrastructure services.</summary>
    public static IServiceCollection AddExecutionServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- Configuration -----------------------------------------------------
        services
            .AddOptions<BinanceOptions>()
            .Bind(configuration.GetSection(BinanceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // --- Persistence ---------------------------------------------------------
        services.AddAgentivaDbContext<ExecutionDbContext>(configuration);
        services.AddAgentivaOutboxProcessor<ExecutionDbContext>(configuration);
        services.AddAgentivaDatabaseMigrator<ExecutionDbContext>(configuration);

        services.AddScoped<IOrderRepository, OrderRepository>();

        // --- Exchange adapters -----------------------------------------------------
        // Both are registered against the same IExchangeExecution interface;
        // IExchangeExecutionResolver picks between them by trading mode at
        // request time. Neither is conditionally registered on configuration,
        // so a mode change takes effect without a restart.
        services.AddScoped<IExchangeExecution, SimulatedExchangeExecution>();

        services.AddHttpClient<IExchangeExecution, BinanceExecutionAdapter>((serviceProvider, client) =>
        {
            var binance = serviceProvider.GetRequiredService<IOptions<BinanceOptions>>().Value;
            client.BaseAddress = new Uri(binance.RestBaseUrl);

            // Order placement is a single POST with no retry semantics of its
            // own — see the remarks on BinanceExecutionAdapter and
            // RiskServiceClient for why a bounded, un-retried timeout is the
            // right shape here, not a generous or resilient one.
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddScoped<IExchangeExecutionResolver, ExchangeExecutionResolver>();

        // --- Application -----------------------------------------------------------
        services.AddAgentivaMediator(typeof(SubmitOrderCommand).Assembly);
        services.AddValidatorsFromAssemblyContaining<SubmitOrderCommandValidator>();

        services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        services.AddPipelineBehavior(typeof(IdempotencyBehavior<,>));

        return services;
    }
}
