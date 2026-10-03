using Agentiva.BuildingBlocks.Application.Abstractions;
using Agentiva.BuildingBlocks.Persistence.Idempotency;
using Shouldly;
using Xunit;

namespace Agentiva.IntegrationTests;

/// <summary>
/// Tests the idempotency store against real PostgreSQL.
/// </summary>
/// <remarks>
/// The guarantee under test is that a duplicate financial command cannot
/// execute twice. It rests entirely on a unique index and on how PostgreSQL
/// behaves when two transactions race to insert the same key, so it can only be
/// verified against a real database.
/// </remarks>
public sealed class IdempotencyStoreTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Claiming_an_unseen_key_succeeds()
    {
        await using var context = await postgres.CreateContextAsync(new FixedClock(Now));
        var store = new EfIdempotencyStore<Agentiva.Risk.Infrastructure.Persistence.RiskDbContext>(
            context, new FixedClock(Now));

        var key = $"claim-{Guid.CreateVersion7()}";

        var claimed = await store.TryClaimAsync(key, "TestCommand", TestContext.Current.CancellationToken);

        claimed.ShouldBeTrue();

        var record = await store.FindAsync(key, TestContext.Current.CancellationToken);
        record.ShouldNotBeNull();
        record!.State.ShouldBe(IdempotencyState.InFlight);
    }

    /// <summary>
    /// The core guarantee: a second claim of the same key is refused.
    /// </summary>
    /// <remarks>
    /// This is what stops an HTTP retry, a RabbitMQ redelivery or a restarted
    /// pod from placing a second exchange order.
    /// </remarks>
    [Fact]
    public async Task Claiming_the_same_key_twice_is_refused()
    {
        await using var context = await postgres.CreateContextAsync(new FixedClock(Now));
        var store = new EfIdempotencyStore<Agentiva.Risk.Infrastructure.Persistence.RiskDbContext>(
            context, new FixedClock(Now));

        var key = $"duplicate-{Guid.CreateVersion7()}";

        (await store.TryClaimAsync(key, "TestCommand", TestContext.Current.CancellationToken))
            .ShouldBeTrue();

        (await store.TryClaimAsync(key, "TestCommand", TestContext.Current.CancellationToken))
            .ShouldBeFalse("a key that is already claimed must not be claimable again");
    }

    /// <summary>
    /// Two concurrent claims: exactly one wins.
    /// </summary>
    /// <remarks>
    /// The realistic failure mode. A read-then-write check would let both
    /// callers see no existing claim and both proceed, producing two orders.
    /// The unique index makes the database the arbiter, and this test is the
    /// only way to prove it works — a single-threaded test cannot.
    /// </remarks>
    [Fact]
    public async Task Concurrent_claims_of_the_same_key_yield_exactly_one_winner()
    {
        var key = $"race-{Guid.CreateVersion7()}";
        const int contenders = 8;

        // Separate contexts: a shared DbContext is not thread-safe, and sharing
        // one would test nothing about database-level concurrency.
        var tasks = Enumerable.Range(0, contenders).Select(async _ =>
        {
            await using var context = await postgres.CreateContextAsync(new FixedClock(Now));
            var store = new EfIdempotencyStore<Agentiva.Risk.Infrastructure.Persistence.RiskDbContext>(
                context, new FixedClock(Now));

            return await store.TryClaimAsync(key, "TestCommand", CancellationToken.None);
        });

        var results = await Task.WhenAll(tasks);

        results.Count(won => won).ShouldBe(
            1,
            "exactly one concurrent caller may claim an idempotency key; more than one means a "
            + "duplicate financial command could execute");
    }

    [Fact]
    public async Task A_completed_key_returns_its_stored_response()
    {
        await using var context = await postgres.CreateContextAsync(new FixedClock(Now));
        var store = new EfIdempotencyStore<Agentiva.Risk.Infrastructure.Persistence.RiskDbContext>(
            context, new FixedClock(Now));

        var key = $"complete-{Guid.CreateVersion7()}";
        const string payload = """{"decision":"Approved","approvedQuantity":"0.01"}""";

        await store.TryClaimAsync(key, "TestCommand", TestContext.Current.CancellationToken);
        await store.CompleteAsync(key, payload, TestContext.Current.CancellationToken);

        var record = await store.FindAsync(key, TestContext.Current.CancellationToken);

        record.ShouldNotBeNull();
        record!.State.ShouldBe(IdempotencyState.Completed);

        // The replay path: the caller gets back exactly what the first call
        // returned, so it cannot distinguish a replay from the original.
        record.ResponsePayload.ShouldBe(payload);
    }

    /// <summary>
    /// A failed key stays poisoned rather than being released.
    /// </summary>
    /// <remarks>
    /// The conservative choice, and the right one. A handler that threw may
    /// already have reached the exchange, so releasing the key for an automatic
    /// retry could place a second live order. The key stays failed and the
    /// operation goes to reconciliation.
    /// </remarks>
    [Fact]
    public async Task A_failed_key_is_not_released_for_retry()
    {
        await using var context = await postgres.CreateContextAsync(new FixedClock(Now));
        var store = new EfIdempotencyStore<Agentiva.Risk.Infrastructure.Persistence.RiskDbContext>(
            context, new FixedClock(Now));

        var key = $"poisoned-{Guid.CreateVersion7()}";

        await store.TryClaimAsync(key, "TestCommand", TestContext.Current.CancellationToken);
        await store.MarkFailedAsync(key, "TimeoutException: exchange did not respond",
            TestContext.Current.CancellationToken);

        var record = await store.FindAsync(key, TestContext.Current.CancellationToken);
        record.ShouldNotBeNull();
        record!.State.ShouldBe(IdempotencyState.Failed);
        record.FailureReason.ShouldContain("TimeoutException");

        // Still unclaimable: a retry must not be able to re-run a command whose
        // outcome is unknown.
        (await store.TryClaimAsync(key, "TestCommand", TestContext.Current.CancellationToken))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task An_unseen_key_returns_no_record()
    {
        await using var context = await postgres.CreateContextAsync(new FixedClock(Now));
        var store = new EfIdempotencyStore<Agentiva.Risk.Infrastructure.Persistence.RiskDbContext>(
            context, new FixedClock(Now));

        var record = await store.FindAsync(
            $"never-seen-{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        record.ShouldBeNull();
    }
}
