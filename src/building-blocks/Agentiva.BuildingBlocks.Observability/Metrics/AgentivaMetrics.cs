using System.Diagnostics.Metrics;

namespace Agentiva.BuildingBlocks.Observability.Metrics;

/// <summary>
/// The platform's business metrics, exported through OpenTelemetry to Prometheus.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately centralised in one type rather than scattered across services.
/// A metric name is an operational contract: dashboards, alert rules and
/// runbooks are all keyed on it, so a renamed counter silently breaks a page
/// that someone relies on at three in the morning. Declaring them together also
/// keeps the unit suffixes and label sets consistent.
/// </para>
/// <para>
/// Label cardinality is kept deliberately low. Symbol and strategy are safe
/// (a handful of values each); order id and correlation id are never labels —
/// they belong in traces and logs, where high cardinality is free. An unbounded
/// label on a Prometheus counter will eventually exhaust the server's memory.
/// </para>
/// </remarks>
public sealed class AgentivaMetrics : IDisposable
{
    /// <summary>Meter name registered with the OpenTelemetry SDK.</summary>
    public const string MeterName = "Agentiva";

    private readonly Meter _meter;

    public AgentivaMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        // --- AI agent platform ------------------------------------------------
        AgentRuns = _meter.CreateCounter<long>(
            "agentiva_agent_runs_total", unit: "{run}",
            description: "AI agent runs started, labelled by agent name.");

        AgentFailures = _meter.CreateCounter<long>(
            "agentiva_agent_failures_total", unit: "{run}",
            description: "AI agent runs that failed or produced output failing schema validation.");

        AgentDuration = _meter.CreateHistogram<double>(
            "agentiva_agent_duration_ms", unit: "ms",
            description: "Wall-clock duration of an AI agent run.");

        // --- Strategy ---------------------------------------------------------
        Signals = _meter.CreateCounter<long>(
            "agentiva_signals_total", unit: "{signal}",
            description: "Trading signals produced, labelled by strategy and action.");

        // --- Risk -------------------------------------------------------------
        RiskChecks = _meter.CreateCounter<long>(
            "agentiva_risk_checks_total", unit: "{check}",
            description: "Risk evaluations performed, labelled by decision.");

        RiskRejections = _meter.CreateCounter<long>(
            "agentiva_risk_rejections_total", unit: "{rejection}",
            description: "Risk rejections, labelled by the first failing check.");

        RiskLimitBreaches = _meter.CreateCounter<long>(
            "agentiva_risk_limit_breaches_total", unit: "{breach}",
            description: "Portfolio-level risk limit breaches, labelled by limit name.");

        // --- Orders and execution ---------------------------------------------
        Orders = _meter.CreateCounter<long>(
            "agentiva_orders_total", unit: "{order}",
            description: "Orders created, labelled by symbol, side and trading mode.");

        OrdersFilled = _meter.CreateCounter<long>(
            "agentiva_orders_filled_total", unit: "{order}",
            description: "Orders that reached a filled state.");

        OrdersFailed = _meter.CreateCounter<long>(
            "agentiva_orders_failed_total", unit: "{order}",
            description: "Orders rejected by the exchange or failed locally, labelled by reason.");

        OrdersIndeterminate = _meter.CreateCounter<long>(
            "agentiva_orders_indeterminate_total", unit: "{order}",
            description: "Orders whose exchange outcome could not be established. Always alert on this.");

        ExecutionLatency = _meter.CreateHistogram<double>(
            "agentiva_execution_latency_ms", unit: "ms",
            description: "Round-trip latency of an order submission to the exchange.");

        // --- Market data -------------------------------------------------------
        MarketDataLatency = _meter.CreateHistogram<double>(
            "agentiva_market_data_latency_ms", unit: "ms",
            description: "Delay between the exchange event timestamp and local receipt.");

        MarketDataStaleEvents = _meter.CreateCounter<long>(
            "agentiva_market_data_stale_total", unit: "{event}",
            description: "Occasions market data aged past its staleness threshold.");

        MarketMessages = _meter.CreateCounter<long>(
            "agentiva_market_messages_total", unit: "{message}",
            description: "Normalised market data messages processed, labelled by kind.");

        // --- Portfolio ---------------------------------------------------------
        PortfolioValue = _meter.CreateGauge<double>(
            "agentiva_portfolio_value", unit: "{quote_asset}",
            description: "Total account value: cash plus marked positions.");

        DailyPnl = _meter.CreateGauge<double>(
            "agentiva_daily_pnl", unit: "{quote_asset}",
            description: "Realised plus unrealised P&L for the current UTC trading day.");

        Drawdown = _meter.CreateGauge<double>(
            "agentiva_drawdown_percent", unit: "%",
            description: "Peak-to-trough decline from the high-water mark.");

        OpenPositions = _meter.CreateGauge<long>(
            "agentiva_open_positions", unit: "{position}",
            description: "Currently open positions.");

        ExposurePercent = _meter.CreateGauge<double>(
            "agentiva_exposure_percent", unit: "%",
            description: "Open position notional as a percentage of total account value.");

        // --- Integrity and operations -------------------------------------------
        ReconciliationFailures = _meter.CreateCounter<long>(
            "agentiva_reconciliation_failures_total", unit: "{failure}",
            description: "Reconciliation runs that found a mismatch. Always alert on this.");

        ExchangeConnectionStatus = _meter.CreateGauge<int>(
            "agentiva_exchange_connection_status", unit: "{status}",
            description: "Exchange connectivity: 1 connected, 0 disconnected.");

        KillSwitchEngaged = _meter.CreateGauge<int>(
            "agentiva_kill_switch_engaged", unit: "{status}",
            description: "Global kill switch: 1 engaged, 0 clear.");

        OutboxBacklog = _meter.CreateGauge<long>(
            "agentiva_outbox_pending", unit: "{message}",
            description: "Unpublished outbox rows. A rising value means events are not reaching consumers.");
    }

    /// <summary>AI agent runs started.</summary>
    public Counter<long> AgentRuns { get; }

    /// <summary>AI agent runs that failed.</summary>
    public Counter<long> AgentFailures { get; }

    /// <summary>Duration of an agent run.</summary>
    public Histogram<double> AgentDuration { get; }

    /// <summary>Trading signals produced.</summary>
    public Counter<long> Signals { get; }

    /// <summary>Risk evaluations performed.</summary>
    public Counter<long> RiskChecks { get; }

    /// <summary>Risk rejections.</summary>
    public Counter<long> RiskRejections { get; }

    /// <summary>Portfolio-level risk limit breaches.</summary>
    public Counter<long> RiskLimitBreaches { get; }

    /// <summary>Orders created.</summary>
    public Counter<long> Orders { get; }

    /// <summary>Orders filled.</summary>
    public Counter<long> OrdersFilled { get; }

    /// <summary>Orders rejected or failed.</summary>
    public Counter<long> OrdersFailed { get; }

    /// <summary>Orders with an unknown exchange outcome.</summary>
    public Counter<long> OrdersIndeterminate { get; }

    /// <summary>Order submission latency.</summary>
    public Histogram<double> ExecutionLatency { get; }

    /// <summary>Market data receipt delay.</summary>
    public Histogram<double> MarketDataLatency { get; }

    /// <summary>Market data staleness occurrences.</summary>
    public Counter<long> MarketDataStaleEvents { get; }

    /// <summary>Market data messages processed.</summary>
    public Counter<long> MarketMessages { get; }

    /// <summary>Total account value.</summary>
    public Gauge<double> PortfolioValue { get; }

    /// <summary>P&amp;L for the current trading day.</summary>
    public Gauge<double> DailyPnl { get; }

    /// <summary>Current drawdown percentage.</summary>
    public Gauge<double> Drawdown { get; }

    /// <summary>Open position count.</summary>
    public Gauge<long> OpenPositions { get; }

    /// <summary>Exposure as a percentage of account value.</summary>
    public Gauge<double> ExposurePercent { get; }

    /// <summary>Reconciliation mismatches.</summary>
    public Counter<long> ReconciliationFailures { get; }

    /// <summary>Exchange connectivity indicator.</summary>
    public Gauge<int> ExchangeConnectionStatus { get; }

    /// <summary>Kill switch indicator.</summary>
    public Gauge<int> KillSwitchEngaged { get; }

    /// <summary>Unpublished outbox backlog.</summary>
    public Gauge<long> OutboxBacklog { get; }

    public void Dispose() => _meter.Dispose();
}
