using Agentiva.BuildingBlocks.Application.Behaviors;
using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Persistence;
using Agentiva.Risk.Application.Abstractions;
using Agentiva.Risk.Application.Evaluations;
using Agentiva.Risk.Infrastructure.Persistence;
using Agentiva.Risk.Infrastructure.Providers;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agentiva.Risk.Infrastructure;

/// <summary>Composition root for the Risk Service.</summary>
public static class RiskInfrastructureExtensions
{
    /// <summary>Registers the risk application and infrastructure services.</summary>
    public static IServiceCollection AddRiskServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- Persistence -------------------------------------------------------
        services.AddAgentivaDbContext<RiskDbContext>(configuration);
        services.AddAgentivaOutboxProcessor<RiskDbContext>(configuration);
        services.AddAgentivaDatabaseMigrator<RiskDbContext>();

        services.AddScoped<IRiskPolicyRepository, RiskPolicyRepository>();
        services.AddScoped<IRiskCheckRepository, RiskCheckRepository>();

        // --- Providers ---------------------------------------------------------
        services.AddScoped<IPlatformStateProvider, PlatformStateProvider>();
        services.AddSingleton<IInstrumentPrecisionProvider, ConfiguredInstrumentPrecisionProvider>();

        services
            .AddOptions<InstrumentPrecisionOptions>()
            .Bind(configuration.GetSection(InstrumentPrecisionOptions.SectionName))
            .ValidateOnStart();

        // --- Application -------------------------------------------------------
        services.AddAgentivaMediator(typeof(EvaluateIntentCommand).Assembly);
        services.AddValidatorsFromAssemblyContaining<EvaluateIntentCommandValidator>();

        // Behavior order is load-bearing and runs outermost first:
        //   1. Logging      — records everything, including rejections.
        //   2. Validation   — rejects malformed input before a key is consumed.
        //   3. Idempotency  — claims the key, then runs the handler.
        services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        services.AddPipelineBehavior(typeof(IdempotencyBehavior<,>));

        // --- Seeding -----------------------------------------------------------
        services.AddHostedService<RiskPolicySeeder>();

        return services;
    }
}
