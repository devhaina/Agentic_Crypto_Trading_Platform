using System.Security.Cryptography;
using System.Text;

namespace Agentiva.Execution.Infrastructure.Binance;

/// <summary>
/// Signs a Binance REST request with HMAC-SHA256, as Binance's <c>SIGNED</c>
/// endpoint type requires.
/// </summary>
/// <remarks>
/// Binance signs the exact, already-assembled query string — not a
/// canonicalised or re-ordered form of it — so the caller must build the
/// query string once, sign that literal string, and append the signature
/// rather than re-deriving it from the individual parameters.
/// </remarks>
public static class BinanceRequestSigner
{
    /// <summary>Computes the lowercase hex HMAC-SHA256 signature of a query string.</summary>
    public static string Sign(string queryString, string apiSecret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(apiSecret);
        var messageBytes = Encoding.UTF8.GetBytes(queryString);

        var hash = HMACSHA256.HashData(keyBytes, messageBytes);

        return Convert.ToHexStringLower(hash);
    }
}
