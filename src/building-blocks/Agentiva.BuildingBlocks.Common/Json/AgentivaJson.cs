using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agentiva.BuildingBlocks.Common.Json;

/// <summary>
/// The single JSON configuration used for HTTP payloads and RabbitMQ envelopes.
/// </summary>
/// <remarks>
/// <para>
/// Shared deliberately: a service that serialises an event differently from the
/// service that consumes it is a silent data-corruption bug. The notable choice
/// here is <c>NumberHandling</c> — financial quantities cross the wire as
/// JSON <em>strings</em>, never as JSON numbers.
/// </para>
/// <para>
/// JSON numbers are IEEE-754 doubles in most parsers (including JavaScript's
/// <c>JSON.parse</c>, which the Angular client uses). Round-tripping a
/// <see cref="decimal"/> price such as <c>100250.123456789012345678</c> through a
/// double loses precision irrecoverably. Emitting it as <c>"100250.123456789012345678"</c>
/// preserves every digit end to end.
/// </para>
/// </remarks>
public static class AgentivaJson
{
    /// <summary>Canonical options instance. Treat as immutable.</summary>
    public static readonly JsonSerializerOptions Options = Create();

    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

            // Write decimals as strings and accept them on read. See remarks.
            NumberHandling = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString,

            // Reject unknown members on the way in. An event shape that has
            // drifted must fail loudly and land in the dead-letter queue rather
            // than be silently truncated.
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));

        // Without this, a Result<T> serialises fine (serialisation only reads
        // properties) but can never be deserialised back — the type has no
        // constructor System.Text.Json's reflection converter can use, by
        // design. That silent asymmetry is exactly what let a financial
        // command's idempotency replay path throw at runtime while every
        // unit test of the same command passed; see the remarks on
        // ResultJsonConverterFactory.
        options.Converters.Add(new Results.ResultJsonConverterFactory());
        return options;
    }

    /// <summary>
    /// Lenient variant for inbound third-party payloads (exchange REST/WebSocket
    /// messages) where unknown fields are expected and must be tolerated.
    /// </summary>
    public static JsonSerializerOptions CreateTolerant()
    {
        var options = Create();
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
        return options;
    }
}
