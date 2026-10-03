using Agentiva.BuildingBlocks.Application.Abstractions;

namespace Agentiva.BuildingBlocks.Persistence.Idempotency;

/// <summary>
/// Durable record of a financial command, keyed by its idempotency key.
/// </summary>
/// <remarks>
/// Stored in PostgreSQL with a unique index on <see cref="Key"/>. The unique
/// index is the actual concurrency control: two simultaneous requests both try
/// to insert, and the database lets exactly one win. A read-then-write check
/// would let both pass under load.
/// </remarks>
public sealed class IdempotencyEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Caller-supplied idempotency key. Uniquely indexed.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>CLR type name of the originating command.</summary>
    public string RequestType { get; set; } = string.Empty;

    public IdempotencyState State { get; set; } = IdempotencyState.InFlight;

    /// <summary>Serialised response, stored on completion so a replay can return it.</summary>
    public string? ResponsePayload { get; set; }

    /// <summary>Failure detail when the outcome is indeterminate.</summary>
    public string? FailureReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
