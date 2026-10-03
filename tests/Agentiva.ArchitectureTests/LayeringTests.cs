using System.Reflection;
using Agentiva.BuildingBlocks.Domain.Primitives;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace Agentiva.ArchitectureTests;

/// <summary>
/// Enforces the Clean Architecture dependency rule.
/// </summary>
/// <remarks>
/// These tests exist because layering decays silently. Nothing stops a developer
/// adding an EF Core reference to a domain project to solve an immediate
/// problem, and nothing will fail until the risk calculations can no longer be
/// unit-tested without a database. A build-time assertion is the only durable
/// defence.
/// </remarks>
public sealed class LayeringTests
{
    private static Assembly RiskDomain => typeof(Risk.Domain.Policies.RiskPolicy).Assembly;
    private static Assembly RiskApplication => typeof(Risk.Application.Evaluations.EvaluateIntentCommand).Assembly;
    private static Assembly TradingDomain => typeof(Trading.Domain.Intents.TradingIntent).Assembly;
    private static Assembly TradingApplication =>
        typeof(Trading.Application.Intents.CreateTradingIntentCommand).Assembly;
    private static Assembly BuildingBlocksDomain => typeof(Symbol).Assembly;

    /// <summary>
    /// A domain layer must not depend on infrastructure.
    /// </summary>
    /// <remarks>
    /// The risk calculations are the most consequential code in the platform and
    /// must remain testable without a database, a broker or a web host. An EF
    /// Core reference here would make every risk test an integration test.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DomainAssemblies))]
    public void Domain_layers_do_not_depend_on_infrastructure(string name, Assembly assembly)
    {
        _ = name;

        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "StackExchange.Redis",
                "RabbitMQ.Client",
                "Microsoft.AspNetCore",
                "Serilog",
                "Swashbuckle",
                "Agentiva.BuildingBlocks.Persistence",
                "Agentiva.BuildingBlocks.Messaging",
                "Agentiva.BuildingBlocks.ServiceDefaults")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result, "depends on infrastructure"));
    }

    /// <summary>
    /// An application layer must not depend on a concrete infrastructure
    /// implementation; it declares interfaces that infrastructure satisfies.
    /// </summary>
    [Theory]
    [MemberData(nameof(ApplicationAssemblies))]
    public void Application_layers_do_not_depend_on_infrastructure(string name, Assembly assembly)
    {
        _ = name;

        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "StackExchange.Redis",
                "RabbitMQ.Client",
                "Microsoft.AspNetCore",
                "Swashbuckle",
                "Agentiva.BuildingBlocks.Persistence",
                "Agentiva.BuildingBlocks.ServiceDefaults")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result, "depends on infrastructure"));
    }

    /// <summary>
    /// A service's domain must not reference another service's assemblies.
    /// </summary>
    /// <remarks>
    /// Cross-service type sharing is how microservices quietly become a
    /// distributed monolith: a change to the trading domain would then force a
    /// redeploy of the risk service. Services share only the published event
    /// contracts.
    /// </remarks>
    [Fact]
    public void Services_do_not_reference_each_other()
    {
        var riskResult = Types.InAssembly(RiskDomain)
            .ShouldNot()
            .HaveDependencyOnAny("Agentiva.Trading")
            .GetResult();

        riskResult.IsSuccessful.ShouldBeTrue(
            FailureMessage(riskResult, "references the trading service"));

        var tradingResult = Types.InAssembly(TradingDomain)
            .ShouldNot()
            .HaveDependencyOnAny("Agentiva.Risk")
            .GetResult();

        tradingResult.IsSuccessful.ShouldBeTrue(
            FailureMessage(tradingResult, "references the risk service"));
    }

    /// <summary>
    /// The published event contracts must stay dependency-free.
    /// </summary>
    /// <remarks>
    /// Every publisher and consumer references this assembly, so any dependency
    /// added here propagates across the whole estate — and would also have to be
    /// satisfiable by the Python agent platform and the Angular client, which
    /// consume the same contracts.
    /// </remarks>
    [Fact]
    public void Event_contracts_depend_only_on_shared_primitives()
    {
        var result = Types.InAssembly(typeof(Contracts.Events.IntegrationEvent).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "RabbitMQ.Client",
                "Npgsql",
                "StackExchange.Redis",
                "Serilog",
                "Agentiva.BuildingBlocks.Persistence",
                "Agentiva.BuildingBlocks.Messaging",
                "Agentiva.BuildingBlocks.Application",
                "Agentiva.BuildingBlocks.ServiceDefaults")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result, "has grown a dependency"));
    }

    /// <summary>The shared domain primitives must not depend on any infrastructure.</summary>
    [Fact]
    public void Shared_domain_primitives_are_infrastructure_free()
    {
        var result = Types.InAssembly(BuildingBlocksDomain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Npgsql",
                "RabbitMQ.Client",
                "StackExchange.Redis",
                "Serilog")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result, "depends on infrastructure"));
    }

    public static TheoryData<string, Assembly> DomainAssemblies() => new()
    {
        { "Risk.Domain", RiskDomain },
        { "Trading.Domain", TradingDomain },
        { "BuildingBlocks.Domain", BuildingBlocksDomain }
    };

    public static TheoryData<string, Assembly> ApplicationAssemblies() => new()
    {
        { "Risk.Application", RiskApplication },
        { "Trading.Application", TradingApplication }
    };

    internal static string FailureMessage(NetArchTest.Rules.TestResult result, string problem)
    {
        var offenders = result.FailingTypeNames is null
            ? "(none reported)"
            : string.Join(", ", result.FailingTypeNames);

        return $"The following types violate the rule ({problem}): {offenders}";
    }
}
