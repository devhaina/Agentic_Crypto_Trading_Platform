using System.Collections.Concurrent;

namespace Agentiva.Strategy.Infrastructure.Candles;

/// <summary>Owns one <see cref="CandleBuffer"/> per (symbol, timeframe) pair for the life of the process.</summary>
public sealed class CandleBufferStore(int capacityPerBuffer)
{
    private readonly ConcurrentDictionary<string, CandleBuffer> _buffers = new(StringComparer.Ordinal);

    public CandleBuffer GetOrCreate(string symbol, string timeframe)
        => _buffers.GetOrAdd(Key(symbol, timeframe), _ => new CandleBuffer(capacityPerBuffer));

    private static string Key(string symbol, string timeframe) => $"{symbol}:{timeframe}";
}
