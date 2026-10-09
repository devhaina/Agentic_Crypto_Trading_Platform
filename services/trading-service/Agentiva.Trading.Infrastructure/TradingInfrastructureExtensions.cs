using System.ComponentModel.DataAnnotations;
using Agentiva.BuildingBlocks.Application.Behaviors;
using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Persistence;
using Agentiva.Trading.Application.Abstractions;
using Agentiva.Trading.Application.Intents;
using Agentiva.Trading.Infrastructure.Clients;
using Agentiva.Trading.Infrastructure.Persistence;
using Agentiva.Trading.Infrastructure.Providers;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agentiva.Trading.Infrastructure;

/// <summary>Downstream service endpoints the Trading Service calls.</summary>
public sealed class DownstreamServiceOptions
{
    public const string SectionName = "Services";

    /// <summary>Base URL of the Risk Service.</summary>
    [Required]
    public string RiskServiceUrl { get; set; } = "http://risk-service:8080";

    /// <summary>Base URL of the Execution Service. Used from Phase 5.</summary>
    public string ExecutionServiceUrl { get; set; } = "http://execution-service:8080";

    /// <summary>Base URL of the Portfolio Service. Used from Phase 6.</summary>
    public string PortfolioServiceUrl { get; set; } = "http://portfolio-service:8080";
}

/// <summary>Composition root for the Trading Service.</summary>
public static class TradingInfrastructureExtensions
{
    /// <summary>Registers the trading application and infrastructure services.</summary>
    public static IServiceCollection AddTradingServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<DownstreamServiceOptions>()
            .Bind(configuration.GetSection(DownstreamServiceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<PaperPortfolioOptions>()
            .Bind(configuration.GetSection(PaperPortfolioOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // --- Persistence -------------------------------------------------------
        services.AddAgentivaDbContext<TradingDbContext>(configuration);
        services.AddAgentivaOutboxProcessor<TradingDbContext>(configuration);
        services.AddAgentivaDatabaseMigrator<TradingDbContext>(configuration);

        services.AddScoped<ITradingIntentRepository, TradingIntentRepository>();

        // --- Risk gate client ----------------------------------------------------
        var riskUrl = configuration["Services:RiskServiceUrl"] ?? "http://risk-service:8080";

        services
            .AddHttpClient<IRiskServiceClient, RiskServiceClient>(client =>
            {
                client.BaseAddress = new Uri(riskUrl);

                // A generous but bounded timeout. Risk evaluation is pure
                // computation plus two short database reads, so anything slower
                // than this indicates a problem — and waiting longer only widens
                // the window in which the decision outcome is unknown.
                client.Timeout = TimeSpan.FromSeconds(15);
            });

        // Note the absence of .AddAgentivaResilience() here, and that it is
        // deliberate. An automatic retry of a timed-out risk evaluation could
        // produce a second approval for the same intent, so this client fails
        // closed instead and the intent is parked as RiskUnavailable. See the
        // remarks on RiskServiceClient.

        // --- Execution Service client ----------------------------------------------
        var executionUrl = configuration["Services:ExecutionServiceUrl"] ?? "http://execution-service:8080";

        services
            .AddHttpClient<IExecutionServiceClient, ExecutionServiceClient>(client =>
            {
                client.BaseAddress = new Uri(executionUrl);
                client.Timeout = TimeSpan.FromSeconds(15);
            });

        // No .AddAgentivaResilience() here either, for the same reason as the
        // risk gate client: a timed-out order placement may already have
        // reached the exchange, and an automatic retry risks a second one.

        // --- Providers -----------------------------------------------------------
        services.AddScoped<IPaperLedgerStore, PaperLedgerStore>();

        // Both interfaces resolve to the same concrete type but as separate
        // scoped instances — harmless here, since the only state it carries
        // (a one-shot warning flag) tolerates being logged twice at worst.
        services.AddScoped<IPortfolioSnapshotProvider, PaperPortfolioSnapshotProvider>();
        services.AddScoped<IPaperLedgerDefaults, PaperPortfolioSnapshotProvider>();
        services.AddScoped<IMarketConditionProvider, RedisMarketConditionProvider>();

        // --- Application ---------------------------------------------------------
        services.AddAgentivaMediator(typeof(CreateTradingIntentCommand).Assembly);
        services.AddValidatorsFromAssemblyContaining<CreateTradingIntentCommandValidator>();

        services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        services.AddPipelineBehavior(typeof(IdempotencyBehavior<,>));

        return services;
    }
}
