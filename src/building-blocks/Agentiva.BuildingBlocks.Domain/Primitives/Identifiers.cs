namespace Agentiva.BuildingBlocks.Domain.Primitives;

/// <summary>
/// Contract for GUID-backed strongly typed identifiers.
/// </summary>
/// <remarks>
/// <para>
/// Every identifier in the platform is a distinct type rather than a bare
/// <see cref="Guid"/>. The reason is concrete: a method such as
/// <c>Reconcile(Guid orderId, Guid intentId)</c> accepts its arguments in either
/// order and compiles cleanly, and a transposition is invisible until a position
/// is attributed to the wrong trade. With distinct types the compiler rejects it.
/// </para>
/// <para>
/// All identifiers use version 7 GUIDs, which embed a timestamp and sort
/// chronologically. That keeps B-tree index inserts sequential rather than random.
/// </para>
/// </remarks>
/// <typeparam name="TSelf">The implementing identifier type.</typeparam>
public interface IStronglyTypedId<out TSelf>
    where TSelf : struct
{
    Guid Value { get; }

    static abstract TSelf From(Guid value);

    static abstract TSelf New();
}

// ---------------------------------------------------------------------------
// Identity and accounts
// ---------------------------------------------------------------------------

public readonly record struct UserId(Guid Value) : IStronglyTypedId<UserId>
{
    public static UserId From(Guid value) => new(value);

    public static UserId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct RoleId(Guid Value) : IStronglyTypedId<RoleId>
{
    public static RoleId From(Guid value) => new(value);

    public static RoleId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct TradingAccountId(Guid Value) : IStronglyTypedId<TradingAccountId>
{
    public static TradingAccountId From(Guid value) => new(value);

    public static TradingAccountId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct ExchangeAccountId(Guid Value) : IStronglyTypedId<ExchangeAccountId>
{
    public static ExchangeAccountId From(Guid value) => new(value);

    public static ExchangeAccountId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

// ---------------------------------------------------------------------------
// Strategy and signals
// ---------------------------------------------------------------------------

public readonly record struct StrategyId(Guid Value) : IStronglyTypedId<StrategyId>
{
    public static StrategyId From(Guid value) => new(value);

    public static StrategyId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct StrategyVersionId(Guid Value) : IStronglyTypedId<StrategyVersionId>
{
    public static StrategyVersionId From(Guid value) => new(value);

    public static StrategyVersionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct SignalId(Guid Value) : IStronglyTypedId<SignalId>
{
    public static SignalId From(Guid value) => new(value);

    public static SignalId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

// ---------------------------------------------------------------------------
// AI agents
// ---------------------------------------------------------------------------

public readonly record struct AgentRunId(Guid Value) : IStronglyTypedId<AgentRunId>
{
    public static AgentRunId From(Guid value) => new(value);

    public static AgentRunId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct AgentDecisionId(Guid Value) : IStronglyTypedId<AgentDecisionId>
{
    public static AgentDecisionId From(Guid value) => new(value);

    public static AgentDecisionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

// ---------------------------------------------------------------------------
// Trading, risk and execution
// ---------------------------------------------------------------------------

public readonly record struct TradingIntentId(Guid Value) : IStronglyTypedId<TradingIntentId>
{
    public static TradingIntentId From(Guid value) => new(value);

    public static TradingIntentId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct RiskPolicyId(Guid Value) : IStronglyTypedId<RiskPolicyId>
{
    public static RiskPolicyId From(Guid value) => new(value);

    public static RiskPolicyId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct RiskCheckId(Guid Value) : IStronglyTypedId<RiskCheckId>
{
    public static RiskCheckId From(Guid value) => new(value);

    public static RiskCheckId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct OrderId(Guid Value) : IStronglyTypedId<OrderId>
{
    public static OrderId From(Guid value) => new(value);

    public static OrderId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct OrderExecutionId(Guid Value) : IStronglyTypedId<OrderExecutionId>
{
    public static OrderExecutionId From(Guid value) => new(value);

    public static OrderExecutionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct TradeId(Guid Value) : IStronglyTypedId<TradeId>
{
    public static TradeId From(Guid value) => new(value);

    public static TradeId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

// ---------------------------------------------------------------------------
// Portfolio
// ---------------------------------------------------------------------------

public readonly record struct PositionId(Guid Value) : IStronglyTypedId<PositionId>
{
    public static PositionId From(Guid value) => new(value);

    public static PositionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct PortfolioId(Guid Value) : IStronglyTypedId<PortfolioId>
{
    public static PortfolioId From(Guid value) => new(value);

    public static PortfolioId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

// ---------------------------------------------------------------------------
// Backtesting, audit and operations
// ---------------------------------------------------------------------------

public readonly record struct BacktestId(Guid Value) : IStronglyTypedId<BacktestId>
{
    public static BacktestId From(Guid value) => new(value);

    public static BacktestId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct AuditEventId(Guid Value) : IStronglyTypedId<AuditEventId>
{
    public static AuditEventId From(Guid value) => new(value);

    public static AuditEventId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct SystemAlertId(Guid Value) : IStronglyTypedId<SystemAlertId>
{
    public static SystemAlertId From(Guid value) => new(value);

    public static SystemAlertId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct ReconciliationResultId(Guid Value) : IStronglyTypedId<ReconciliationResultId>
{
    public static ReconciliationResultId From(Guid value) => new(value);

    public static ReconciliationResultId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
