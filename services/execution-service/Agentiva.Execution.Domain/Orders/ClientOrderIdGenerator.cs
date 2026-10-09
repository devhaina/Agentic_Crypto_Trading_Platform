using System.Security.Cryptography;
using System.Text;

namespace Agentiva.Execution.Domain.Orders;

/// <summary>
/// Derives the deterministic client order id sent to the exchange.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a pure function of the symbol, the day and the command's own
/// idempotency key, rather than a stored sequence counter. A counter would
/// need its own durable, race-free allocation; hashing the idempotency key
/// needs none, and it gives exactly the property that matters here: replaying
/// the same financial command — the same business operation — always derives
/// the same client order id, so a retry that slips past the idempotency
/// store is still caught by the exchange itself rejecting a duplicate
/// <c>newClientOrderId</c>.
/// </para>
/// <para>
/// This is the second, independent line of defence the <c>OrderCreated</c>
/// event's remarks describe — the idempotency store is the first.
/// </para>
/// </remarks>
public static class ClientOrderIdGenerator
{
    private const string Prefix = "AGENTIVA";

    /// <summary>
    /// Binance's <c>newClientOrderId</c> accepts at most 36 characters.
    /// </summary>
    public const int MaxLength = 36;

    /// <summary>Builds a client order id of the form <c>AGENTIVA-BTCUSDT-20261003-9f3a1c2d</c>.</summary>
    public static string Generate(string symbol, string idempotencyKey, DateTimeOffset now)
    {
        var day = now.UtcDateTime.ToString("yyyyMMdd");
        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)))[..8];

        // Symbols are already exchange-safe (alphanumeric, uppercase), so no
        // further sanitisation is needed before it goes into the id.
        var candidate = $"{Prefix}-{symbol}-{day}-{hash}";

        return candidate.Length <= MaxLength
            ? candidate
            : candidate[..MaxLength];
    }
}
