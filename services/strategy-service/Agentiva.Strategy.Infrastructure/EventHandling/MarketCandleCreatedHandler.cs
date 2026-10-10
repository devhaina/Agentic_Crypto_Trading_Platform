using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.BuildingBlocks.Messaging.Abstractions;
using Agentiva.Contracts.Events;
using Agentiva.Contracts.Events.Market;
using Agentiva.Strategy.Application.Abstractions;
using Agentiva.Strategy.Domain.Entities;
using Agentiva.BuildingBlocks.TradingRules.Indicators;
using Agentiva.BuildingBlocks.TradingRules.Strategies;
using Agentiva.Strategy.Infrastructure.Caching;
using Agentiva.Strategy.Infrastructure.Candles;
using Agentiva.Strategy.Infrastructure.Persistence;
using Agentiva.Strategy.Infrastructure.TimeSeries;
using Microsoft.Extensions.Logging;

namespace Agentiva.Strategy.Infrastructure.EventHandling;

/// <summary>
/// On every closed candle: extend this symbol's rolling window, compute and
/// persist indicators, evaluate every active strategy, and record whatever
/// signals come out of it.
/// </summary>
/// <remarks>
/// Idempotency is handled upstream, not here: <c>RabbitMqConsumerService</c>
/// checks this handler's registered name against the inbox before ever
/// calling <see cref="HandleAsync(MarketCandleCreated,IntegrationEventContext,CancellationToken)"/>,
/// so a redelivered candle never reaches this method twice. That is what
/// makes it safe for this handler to simply evaluate and insert, with no
/// de-duplication logic of its own.
/// </remarks>
public sealed class MarketCandleCreatedHandler(
    CandleBufferStore buffers,
    IndicatorSnapshotWriter indicatorWriter,
    VolatilityCache volatilityCache,
    IStrategyDefinitionRepository strategyDefinitions,
    ISignalRepository signalRepository,
    StrategyDbContext context,
    IEnumerable<IStrategy> strategies,
    ILogger<MarketCandleCreatedHandler> logger)
    : IntegrationEventHandlerBase<MarketCandleCreated>
{
    /// <summary>The indicator engine's own version, recorded alongside each snapshot.</summary>
    public const string EngineVersion = "1.0.0";

    public override string EventType => EventTypes.Market.CandleCreated;

    public override async Task HandleAsync(
        MarketCandleCreated integrationEvent, IntegrationEventContext eventContext, CancellationToken cancellationToken)
    {
        var buffer = buffers.GetOrCreate(integrationEvent.Symbol, integrationEvent.Timeframe);

        buffer.Append(new PriceBar(
            integrationEvent.OpenTime,
            integrationEvent.Open,
            integrationEvent.High,
            integrationEvent.Low,
            integrationEvent.Close,
            integrationEvent.Volume));

        var bars = buffer.Snapshot();
        var indicators = IndicatorSet.Compute(bars);
        var computedAt = integrationEvent.CloseTime;

        var indicatorMap = indicators.ToDictionary();
        if (indicatorMap.Count > 0)
        {
            await indicatorWriter.WriteAsync(
                integrationEvent.Symbol, integrationEvent.Timeframe, indicators, EngineVersion, computedAt,
                cancellationToken);
        }

        // Feeds the Risk Service's MaxVolatilityPercent check via the Trading
        // Service's RedisMarketConditionProvider — see VolatilityCache's own
        // remarks for why this is a separate key from the Market Data
        // Service's tick cache rather than a field merged into it.
        var volatilityPercent = IndicatorEngine.AnnualizedVolatilityPercent(
            indicators.Atr14, bars[^1].Close, integrationEvent.Timeframe);

        if (volatilityPercent is not null)
        {
            await volatilityCache.SetAsync(
                integrationEvent.Symbol, integrationEvent.Timeframe, volatilityPercent.Value, computedAt,
                cancellationToken);
        }

        var evaluationContext = new StrategyEvaluationContext(
            integrationEvent.Symbol, integrationEvent.Timeframe, bars, indicators);

        var active = await strategyDefinitions.ListActiveAsync(cancellationToken);
        var activeByName = active.ToDictionary(s => s.Name, StringComparer.Ordinal);

        foreach (var strategy in strategies)
        {
            if (!activeByName.TryGetValue(strategy.Name, out var definition))
            {
                // A strategy the code knows about but the operator has
                // deactivated, or that has not been seeded yet. Either way,
                // not this handler's decision to make.
                continue;
            }

            var result = strategy.Evaluate(evaluationContext);

            if (result.Action == TradeAction.Hold)
            {
                logger.LogDebug(
                    "{Strategy} held for {Symbol} {Timeframe}: {Reasons}",
                    strategy.Name, integrationEvent.Symbol, integrationEvent.Timeframe,
                    string.Join(',', result.ReasonCodes));
                continue;
            }

            var signal = Signal.Create(
                definition.Id,
                definition.Name,
                definition.CurrentVersion,
                integrationEvent.Symbol,
                integrationEvent.Timeframe,
                result.Action,
                result.Confidence.AsFraction,
                result.EntryPrice,
                result.StopLoss,
                result.TakeProfit,
                result.ReasonCodes,
                computedAt);

            signalRepository.Add(signal);

            logger.LogInformation(
                "{Strategy} produced {Action} for {Symbol} {Timeframe} at {Price} (confidence {Confidence:P0}): {Reasons}",
                strategy.Name, result.Action, integrationEvent.Symbol, integrationEvent.Timeframe, result.EntryPrice,
                result.Confidence.AsFraction, string.Join(',', result.ReasonCodes));
        }

        // One SaveChanges for every signal this candle produced: all of them,
        // or a failure that leaves none of them half-written.
        await context.SaveChangesAsync(cancellationToken);
    }
}
