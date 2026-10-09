using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.Execution.Application.Abstractions;

/// <summary>Default <see cref="IExchangeExecutionResolver"/>, picking by <see cref="IExchangeExecution.SupportsLiveTrading"/>.</summary>
/// <remarks>
/// A plain predicate over the registered adapters rather than keyed DI: the
/// set of adapters is small and fixed (one simulated, one real), and this
/// keeps the Application layer free of any DI-container-specific attribute.
/// </remarks>
public sealed class ExchangeExecutionResolver(IEnumerable<IExchangeExecution> adapters) : IExchangeExecutionResolver
{
    public IExchangeExecution Resolve(TradingMode mode)
    {
        var wantsLive = mode == TradingMode.Live;

        return adapters.FirstOrDefault(a => a.SupportsLiveTrading == wantsLive)
            ?? throw new InvalidOperationException(
                $"No exchange execution adapter is registered for {(wantsLive ? "live" : "simulated")} trading.");
    }
}
