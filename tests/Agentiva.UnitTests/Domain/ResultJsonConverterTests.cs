using Agentiva.BuildingBlocks.Common.Json;
using Agentiva.BuildingBlocks.Common.Results;
using Shouldly;
using System.Text.Json;
using Xunit;

namespace Agentiva.UnitTests.Domain;

/// <summary>
/// Tests the <see cref="Result"/>/<see cref="Result{TValue}"/> JSON round trip.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a real production-shaped bug caught only by running
/// a built container against a live database: <c>Result&lt;T&gt;</c> has no
/// constructor System.Text.Json's reflection converter can use, so it
/// serialises without error but throws <see cref="NotSupportedException"/> on
/// deserialisation. A handler-level test never notices, because it inspects
/// the <c>Result</c> a handler returns directly and never serialises it. The
/// financial-command idempotency replay path is the one place a
/// <c>Result&lt;T&gt;</c> genuinely round-trips through JSON — see
/// <c>IdempotencyBehavior.ReplayOf</c> — and that is exactly the path these
/// tests exercise.
/// </para>
/// </remarks>
public sealed class ResultJsonConverterTests
{
    private sealed record Payload(string Name, decimal Amount);

    [Fact]
    public void A_successful_result_round_trips()
    {
        var original = Result.Success(new Payload("BTCUSDT", 100000.50m));

        var json = JsonSerializer.Serialize(original, AgentivaJson.Options);
        var restored = JsonSerializer.Deserialize<Result<Payload>>(json, AgentivaJson.Options);

        restored.ShouldNotBeNull();
        restored.IsSuccess.ShouldBeTrue();
        restored.Value.Name.ShouldBe("BTCUSDT");
        restored.Value.Amount.ShouldBe(100000.50m);
    }

    [Fact]
    public void A_failed_result_round_trips_with_its_error_intact()
    {
        var original = Result.Failure<Payload>(
            Error.Validation("risk.rejected.max_daily_loss", "Daily loss limit reached."));

        var json = JsonSerializer.Serialize(original, AgentivaJson.Options);
        var restored = JsonSerializer.Deserialize<Result<Payload>>(json, AgentivaJson.Options);

        restored.ShouldNotBeNull();
        restored.IsFailure.ShouldBeTrue();
        restored.Error.Code.ShouldBe("risk.rejected.max_daily_loss");
        restored.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void The_non_generic_result_round_trips()
    {
        var success = Result.Success();
        var successJson = JsonSerializer.Serialize(success, AgentivaJson.Options);
        JsonSerializer.Deserialize<Result>(successJson, AgentivaJson.Options)!.IsSuccess.ShouldBeTrue();

        var failure = Result.Failure(Error.Conflict("test.conflict", "A conflict occurred."));
        var failureJson = JsonSerializer.Serialize(failure, AgentivaJson.Options);
        var restoredFailure = JsonSerializer.Deserialize<Result>(failureJson, AgentivaJson.Options)!;
        restoredFailure.IsFailure.ShouldBeTrue();
        restoredFailure.Error.Code.ShouldBe("test.conflict");
    }

    /// <summary>
    /// Decimal precision must survive the round trip untouched.
    /// </summary>
    /// <remarks>
    /// The idempotency replay path is itself a financial-command path; a
    /// quantity or price that loses a digit on replay would silently
    /// misreport what was actually approved.
    /// </remarks>
    [Fact]
    public void Decimal_precision_in_the_payload_is_preserved()
    {
        var precise = new Payload("ETHUSDT", 0.000000000000000001m);
        var original = Result.Success(precise);

        var json = JsonSerializer.Serialize(original, AgentivaJson.Options);
        var restored = JsonSerializer.Deserialize<Result<Payload>>(json, AgentivaJson.Options)!;

        restored.Value.Amount.ShouldBe(0.000000000000000001m);
    }

    /// <summary>
    /// Reproduces the exact failure mode found in the container smoke test:
    /// a nested generic Result, matching what a financial command endpoint
    /// actually returns.
    /// </summary>
    [Fact]
    public void A_nested_generic_result_type_round_trips()
    {
        var original = Result.Success(new RiskEvaluationLikeResponse("Approved", "0.00999"));

        var json = JsonSerializer.Serialize(original, AgentivaJson.Options);

        // Before the converter existed, this line threw NotSupportedException.
        var restored = JsonSerializer.Deserialize<Result<RiskEvaluationLikeResponse>>(
            json, AgentivaJson.Options);

        restored.ShouldNotBeNull();
        restored.IsSuccess.ShouldBeTrue();
        restored.Value.Decision.ShouldBe("Approved");
        restored.Value.ApprovedQuantity.ShouldBe("0.00999");
    }

    private sealed record RiskEvaluationLikeResponse(string Decision, string ApprovedQuantity);
}
