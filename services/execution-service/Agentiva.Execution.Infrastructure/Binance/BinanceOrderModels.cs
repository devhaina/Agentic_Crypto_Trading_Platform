using System.Text.Json.Serialization;

namespace Agentiva.Execution.Infrastructure.Binance;

/// <summary>Wire shape of a Binance <c>POST /api/v3/order</c> response (<c>newOrderRespType=FULL</c>).</summary>
internal sealed class BinanceOrderResponse
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("orderId")]
    public long OrderId { get; set; }

    [JsonPropertyName("clientOrderId")]
    public string ClientOrderId { get; set; } = string.Empty;

    /// <summary>Server-side transaction time, epoch milliseconds.</summary>
    [JsonPropertyName("transactTime")]
    public long TransactTime { get; set; }

    [JsonPropertyName("executedQty")]
    public decimal ExecutedQty { get; set; }

    /// <summary>Total quote-asset value transacted so far.</summary>
    [JsonPropertyName("cummulativeQuoteQty")]
    public decimal CummulativeQuoteQty { get; set; }

    /// <summary><c>NEW</c>, <c>PARTIALLY_FILLED</c>, <c>FILLED</c> and so on.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Present only with <c>newOrderRespType=FULL</c>; one entry per partial match at placement time.</summary>
    [JsonPropertyName("fills")]
    public List<BinanceFill>? Fills { get; set; }
}

internal sealed class BinanceFill
{
    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("qty")]
    public decimal Qty { get; set; }

    [JsonPropertyName("commission")]
    public decimal Commission { get; set; }

    [JsonPropertyName("commissionAsset")]
    public string CommissionAsset { get; set; } = string.Empty;
}

/// <summary>Wire shape of a Binance REST error body, e.g. <c>{"code":-2010,"msg":"..."}</c>.</summary>
internal sealed class BinanceErrorResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string Msg { get; set; } = string.Empty;
}
