using Agentiva.Execution.Domain.Orders;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.Execution;

/// <summary>
/// Tests for <see cref="ClientOrderIdGenerator"/>'s determinism — the second
/// line of defence against a duplicate order, independent of the idempotency
/// store.
/// </summary>
public sealed class ClientOrderIdGeneratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_same_idempotency_key_always_derives_the_same_client_order_id()
    {
        var first = ClientOrderIdGenerator.Generate("BTCUSDT", "abc-123", Now);
        var second = ClientOrderIdGenerator.Generate("BTCUSDT", "abc-123", Now);

        first.ShouldBe(second);
    }

    [Fact]
    public void Different_idempotency_keys_derive_different_client_order_ids()
    {
        var first = ClientOrderIdGenerator.Generate("BTCUSDT", "abc-123", Now);
        var second = ClientOrderIdGenerator.Generate("BTCUSDT", "abc-124", Now);

        first.ShouldNotBe(second);
    }

    [Fact]
    public void The_id_carries_the_symbol_and_the_UTC_day()
    {
        var id = ClientOrderIdGenerator.Generate("BTCUSDT", "abc-123", Now);

        id.ShouldStartWith("AGENTIVA-BTCUSDT-20261003-");
    }

    [Fact]
    public void The_id_never_exceeds_Binances_client_order_id_length_limit()
    {
        var id = ClientOrderIdGenerator.Generate("1000SATSUSDT", "a-very-long-idempotency-key-used-by-a-caller", Now);

        id.Length.ShouldBeLessThanOrEqualTo(ClientOrderIdGenerator.MaxLength);
    }
}
