using Agentiva.BuildingBlocks.Application.Behaviors;
using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.Persistence.Idempotency;
using Agentiva.Risk.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace Agentiva.IntegrationTests;

/// <summary>
/// Exercises <see cref="IdempotencyBehavior{TRequest,TResponse}"/> end to end
/// against a real PostgreSQL-backed <see cref="EfIdempotencyStore{TContext}"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the regression test for a real production-shaped bug: a financial
/// command's <c>Result&lt;T&gt;</c> response serialised correctly on first
/// success but threw <see cref="NotSupportedException"/> deserialising on
/// replay, because <c>Result&lt;T&gt;</c> has no constructor System.Text.Json's
/// reflection converter can use. That bug was invisible to every test that
/// inspected a handler's returned <c>Result</c> directly — <see cref="IdempotencyStoreTests"/>
/// included, which exercises the store but hands it a hand-written JSON
/// string rather than a genuinely serialised <c>Result&lt;T&gt;</c>. It
/// surfaced only by running a built container against a live database and
/// replaying a real idempotency key through the real HTTP pipeline.
/// </para>
/// <para>
/// This test closes that gap permanently: it runs the actual behavior, with
/// the actual shared <c>AgentivaJson</c> options, against a real database, so
/// a regression in either the JSON converter or the behavior's serialise/
/// deserialise logic fails a fast test in CI rather than a live replay.
/// </para>
/// </remarks>
public sealed class IdempotencyBehaviorTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A response shape matching what a real financial command returns: a nested record inside Result&lt;T&gt;.</summary>
    private sealed record SizedOrderResponse(Guid OrderId, string Symbol, decimal ApprovedQuantity, decimal RiskAmount);

    private sealed record PlaceSizedOrder(string IdempotencyKey, decimal Quantity)
        : IFinancialCommand<Result<SizedOrderResponse>>;

    private sealed class PlaceSizedOrderHandler(int[] callCount)
        : IRequestHandler<PlaceSizedOrder, Result<SizedOrderResponse>>
    {
        public Task<Result<SizedOrderResponse>> HandleAsync(
            PlaceSizedOrder request, CancellationToken cancellationToken)
        {
            // Counts real executions. If the idempotency guard ever failed to
            // intercept a replay, this would increment on the second call --
            // which is exactly a duplicate financial side effect.
            callCount[0]++;

            return Task.FromResult(Result.Success(new SizedOrderResponse(
                Guid.CreateVersion7(), "BTCUSDT", request.Quantity, request.Quantity * 2000m)));
        }
    }

    private sealed record FinancialFailureCommand(string IdempotencyKey)
        : IFinancialCommand<Result<SizedOrderResponse>>;

    private sealed class FinancialFailureHandler
        : IRequestHandler<FinancialFailureCommand, Result<SizedOrderResponse>>
    {
        public Task<Result<SizedOrderResponse>> HandleAsync(
            FinancialFailureCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Failure<SizedOrderResponse>(
                Error.Validation("risk.rejected.max_daily_loss", "Daily loss limit reached.")));
    }

    [Fact]
    public async Task A_successful_financial_command_replays_the_identical_response_without_re_executing()
    {
        await using var context = await postgres.CreateContextAsync(new FixedClock(Now));
        var store = new EfIdempotencyStore<RiskDbContext>(context, new FixedClock(Now));

        var callCount = new[] { 0 };
        var behavior = new IdempotencyBehavior<PlaceSizedOrder, Result<SizedOrderResponse>>(
            store, Microsoft.Extensions.Logging.Abstractions.NullLogger<
                IdempotencyBehavior<PlaceSizedOrder, Result<SizedOrderResponse>>>.Instance);

        var key = $"replay-success-{Guid.CreateVersion7()}";
        var command = new PlaceSizedOrder(key, 0.015m);
        var handler = new PlaceSizedOrderHandler(callCount);

        RequestHandlerDelegate<Result<SizedOrderResponse>> next =
            ct => handler.HandleAsync(command, ct);

        // First delivery: executes for real.
        var first = await behavior.HandleAsync(command, next, TestContext.Current.CancellationToken);

        // Second delivery of the same key: this is the exact call that threw
        // NotSupportedException before the fix.
        var replay = await behavior.HandleAsync(command, next, TestContext.Current.CancellationToken);

        callCount[0].ShouldBe(1, "the handler must execute exactly once; the replay must not re-run it");

        first.IsSuccess.ShouldBeTrue();
        replay.IsSuccess.ShouldBeTrue();

        // The replay must be the SAME outcome, not a freshly computed one --
        // proving it came from the stored payload rather than a second execution.
        replay.Value.OrderId.ShouldBe(first.Value.OrderId);
        replay.Value.ApprovedQuantity.ShouldBe(first.Value.ApprovedQuantity);
        replay.Value.RiskAmount.ShouldBe(first.Value.RiskAmount);
    }

    [Fact]
    public async Task A_failed_financial_command_poisons_the_key_rather_than_replaying()
    {
        await using var context = await postgres.CreateContextAsync(new FixedClock(Now));
        var store = new EfIdempotencyStore<RiskDbContext>(context, new FixedClock(Now));

        var behavior = new IdempotencyBehavior<FinancialFailureCommand, Result<SizedOrderResponse>>(
            store, Microsoft.Extensions.Logging.Abstractions.NullLogger<
                IdempotencyBehavior<FinancialFailureCommand, Result<SizedOrderResponse>>>.Instance);

        var key = $"poisoned-{Guid.CreateVersion7()}";
        var command = new FinancialFailureCommand(key);
        var handler = new FinancialFailureHandler();

        RequestHandlerDelegate<Result<SizedOrderResponse>> next =
            ct => handler.HandleAsync(command, ct);

        // A handler returning Result.Failure is a normal outcome, not an
        // exception -- the behavior must let it through and mark the key
        // completed (replayable), not poisoned. Poisoning is reserved for a
        // thrown exception, where the true outcome is genuinely unknown.
        var first = await behavior.HandleAsync(command, next, TestContext.Current.CancellationToken);
        first.IsFailure.ShouldBeTrue();

        var replay = await behavior.HandleAsync(command, next, TestContext.Current.CancellationToken);

        replay.IsFailure.ShouldBeTrue();
        replay.Error.Code.ShouldBe("risk.rejected.max_daily_loss");
    }
}
