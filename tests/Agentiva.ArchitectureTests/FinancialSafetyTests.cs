using System.Reflection;
using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Shouldly;
using Xunit;

namespace Agentiva.ArchitectureTests;

/// <summary>
/// Enforces the platform's financial safety rules at build time.
/// </summary>
/// <remarks>
/// Each rule here encodes a mistake that is cheap to make, invisible in review,
/// and expensive in production. A code review catches these inconsistently; a
/// test catches them every time.
/// </remarks>
public sealed class FinancialSafetyTests
{
    private static readonly Assembly[] FinancialAssemblies =
    [
        typeof(Symbol).Assembly,
        typeof(Contracts.Events.IntegrationEvent).Assembly,
        typeof(Risk.Domain.Policies.RiskPolicy).Assembly,
        typeof(Risk.Application.Evaluations.EvaluateIntentCommand).Assembly,
        typeof(Trading.Domain.Intents.TradingIntent).Assembly,
        typeof(Trading.Application.Intents.CreateTradingIntentCommand).Assembly,
        typeof(MarketData.Domain.Entities.Tick).Assembly,
        typeof(MarketData.Application.Queries.GetTickerQuery).Assembly,
        typeof(Strategy.Domain.Entities.Signal).Assembly,
        typeof(Strategy.Application.Queries.GetRecentSignalsQuery).Assembly,
        typeof(Execution.Domain.Orders.Order).Assembly,
        typeof(Execution.Application.Orders.SubmitOrderCommand).Assembly,
        typeof(Portfolio.Domain.Positions.Position).Assembly,
        typeof(Portfolio.Application.Positions.GetPositionsQuery).Assembly,
        typeof(Backtesting.Domain.Runs.BacktestRun).Assembly,
        typeof(Backtesting.Application.Commands.RunBacktestCommand).Assembly
    ];

    /// <summary>
    /// Members whose name makes clear they measure time or a count, not money.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A deliberate allow-list rather than a name-pattern heuristic. A pattern
    /// such as "ends with Seconds" would quietly permit a future
    /// <c>PriceInSeconds</c>, and more importantly it would permit a genuinely
    /// monetary field that happened to match. Every exemption should be a
    /// conscious, reviewable decision, so new ones must be added here by hand.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> AllowedNonMonetaryFloatingPointMembers = new(StringComparer.Ordinal)
    {
        "MarketConditionDto.MarketDataAgeSeconds",
        "EvaluateIntentCommand.MarketDataAgeSeconds",
        "RiskEvaluationRequestDto.MarketDataAgeSeconds",
        "RiskPolicyDto.MarketDataStalenessThresholdSeconds",
        "SymbolFeedStatusDto.StaleForSeconds",
        "CreateRiskPolicyCommand.MarketDataStalenessThresholdSeconds",
        "UpdateRiskPolicyCommand.MarketDataStalenessThresholdSeconds"
    };

    /// <summary>
    /// No financial type may expose a <c>float</c> or <c>double</c> member.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Binary floating point cannot represent 0.1 exactly. Accumulating order
    /// fills in a <c>double</c> therefore drifts the recorded position away from
    /// the real one, and the drift compounds without bound over a trading day —
    /// the error is silent, and it is in the books rather than in a log.
    /// </para>
    /// <para>
    /// This is the single most important rule in the suite, which is why it is
    /// asserted over properties <em>and</em> fields, and across the domain,
    /// application and published-contract assemblies alike.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_financial_type_uses_floating_point()
    {
        var offenders = new List<string>();

        foreach (var assembly in FinancialAssemblies)
        {
            foreach (var type in assembly.GetTypes().Where(IsInspectableType))
            {
                foreach (var property in type.GetProperties(
                             BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (IsFloatingPoint(property.PropertyType)
                        && !IsAllowed(type, property.Name))
                    {
                        offenders.Add($"{type.FullName}.{property.Name} : {property.PropertyType.Name}");
                    }
                }

                foreach (var field in type.GetFields(
                             BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    // Compiler-generated backing fields mirror their property,
                    // which is reported separately; skip the duplicate.
                    if (field.Name.Contains("k__BackingField", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (IsFloatingPoint(field.FieldType) && !IsAllowed(type, field.Name))
                    {
                        offenders.Add($"{type.FullName}.{field.Name} : {field.FieldType.Name}");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            "These members use binary floating point. Financial values must use decimal "
            + "(NUMERIC(38,18) in PostgreSQL). If a member genuinely measures time or a count, "
            + "add it to AllowedNonMonetaryFloatingPointMembers with a justification:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Any command that can cause a financial transaction must be idempotent.
    /// </summary>
    /// <remarks>
    /// A command reaches the platform more than once for entirely routine
    /// reasons: an HTTP retry, a RabbitMQ redelivery, a pod restart. Without an
    /// idempotency key each delivery is a fresh order. This test makes
    /// forgetting the key a build failure rather than a duplicate position.
    /// </remarks>
    [Fact]
    public void Commands_that_move_money_carry_an_idempotency_key()
    {
        var offenders = new List<string>();

        // Command names whose effects are confined to this service's own
        // records and cannot reach an exchange. Reads and pure queries are
        // excluded by the ICommand filter already.
        var nonFinancialCommands = new HashSet<string>(StringComparer.Ordinal)
        {
            // Risk policy CRUD: administrative changes to the limits a trade is
            // measured against, not a transaction themselves. A replayed
            // request produces the same policy state, not a duplicate order.
            nameof(Risk.Application.Policies.CreateRiskPolicyCommand),
            nameof(Risk.Application.Policies.UpdateRiskPolicyCommand),
            nameof(Risk.Application.Policies.DeactivateRiskPolicyCommand),
            nameof(Risk.Application.Policies.MarkRiskPolicyAsDefaultCommand),

            // Kill switch: an operator toggle, naturally idempotent (engaging
            // an already-engaged switch, or releasing an already-released
            // one, both leave the same end state) and never itself a trade.
            nameof(Risk.Application.Operations.EngageKillSwitchCommand),
            nameof(Risk.Application.Operations.ReleaseKillSwitchCommand)
        };

        foreach (var assembly in new[]
                 {
                     typeof(Risk.Application.Evaluations.EvaluateIntentCommand).Assembly,
                     typeof(Trading.Application.Intents.CreateTradingIntentCommand).Assembly,
                     typeof(Execution.Application.Orders.SubmitOrderCommand).Assembly
                 })
        {
            foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
            {
                var isCommand = type.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

                var isFinancial = type.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IFinancialCommand<>));

                if (isCommand && !isFinancial && !nonFinancialCommands.Contains(type.Name))
                {
                    offenders.Add(type.FullName ?? type.Name);
                }
            }
        }

        offenders.ShouldBeEmpty(
            "These commands mutate state but do not implement IFinancialCommand<T>, so they are "
            + "not covered by the idempotency behavior. Either implement it, or add the command to "
            + "the nonFinancialCommands allow-list with a justification:\n  "
            + string.Join("\n  ", offenders));
    }

    private static bool IsInspectableType(Type type)
        => type is { IsInterface: false, IsEnum: false }
           && !type.Name.StartsWith('<')
           && type.Namespace is not null
           && type.Namespace.StartsWith("Agentiva", StringComparison.Ordinal);

    private static bool IsFloatingPoint(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(double) || underlying == typeof(float);
    }

    private static bool IsAllowed(Type type, string memberName)
        => AllowedNonMonetaryFloatingPointMembers.Contains($"{type.Name}.{memberName}");

}
