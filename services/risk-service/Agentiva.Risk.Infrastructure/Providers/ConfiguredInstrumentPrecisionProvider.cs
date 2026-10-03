using System.ComponentModel.DataAnnotations;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Risk.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentiva.Risk.Infrastructure.Providers;

/// <summary>Exchange filters for the symbols in scope, read from configuration.</summary>
public sealed class InstrumentPrecisionOptions
{
    public const string SectionName = "InstrumentPrecision";

    /// <summary>Per-symbol filters, keyed by normalised symbol.</summary>
    public Dictionary<string, InstrumentPrecisionEntry> Symbols { get; set; } = [];
}

/// <summary>Exchange filters for one symbol.</summary>
public sealed class InstrumentPrecisionEntry
{
    [Range(typeof(decimal), "0.000000000000000001", "1000000")]
    public decimal TickSize { get; set; }

    [Range(typeof(decimal), "0.000000000000000001", "1000000")]
    public decimal StepSize { get; set; }

    public decimal MinQuantity { get; set; }

    public decimal MaxQuantity { get; set; } = 1_000_000m;

    public decimal MinNotional { get; set; }

    [Required]
    public string BaseAsset { get; set; } = string.Empty;

    [Required]
    public string QuoteAsset { get; set; } = string.Empty;
}

/// <summary>
/// Serves exchange precision from configuration.
/// </summary>
/// <remarks>
/// <para>
/// A Phase 1 implementation. From Phase 2 the Market Data Service loads these
/// filters from the exchange at startup and refreshes them periodically, because
/// exchanges change them without notice and a stale step size causes every order
/// on the symbol to be rejected.
/// </para>
/// <para>
/// An unconfigured symbol returns <c>null</c> rather than a permissive default.
/// Guessing a step size would produce orders the exchange rejects, and guessing
/// a minimum notional could let through a position too small to carry its own
/// fees.
/// </para>
/// </remarks>
public sealed class ConfiguredInstrumentPrecisionProvider(
    IOptions<InstrumentPrecisionOptions> options,
    ILogger<ConfiguredInstrumentPrecisionProvider> logger)
    : IInstrumentPrecisionProvider
{
    private readonly InstrumentPrecisionOptions _options = options.Value;

    public Task<InstrumentPrecision?> GetAsync(Symbol symbol, CancellationToken cancellationToken)
    {
        if (!_options.Symbols.TryGetValue(symbol.Value, out var entry))
        {
            logger.LogWarning(
                "No exchange precision is configured for {Symbol}. Configured symbols: {Configured}.",
                symbol,
                string.Join(", ", _options.Symbols.Keys));

            return Task.FromResult<InstrumentPrecision?>(null);
        }

        var precision = new InstrumentPrecision
        {
            TickSize = entry.TickSize,
            StepSize = entry.StepSize,
            MinQuantity = entry.MinQuantity,
            MaxQuantity = entry.MaxQuantity,
            MinNotional = entry.MinNotional,
            BaseAsset = AssetCode.Create(entry.BaseAsset),
            QuoteAsset = AssetCode.Create(entry.QuoteAsset)
        };

        return Task.FromResult<InstrumentPrecision?>(precision);
    }
}
