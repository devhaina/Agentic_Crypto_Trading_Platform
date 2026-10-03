namespace Agentiva.BuildingBlocks.Common.Abstractions;

/// <summary>
/// Abstraction over the system clock.
/// </summary>
/// <remarks>
/// Time is an input to risk decisions (daily loss windows, market-data staleness,
/// order expiry), so it must be injectable and therefore testable. Production
/// code must never call <c>DateTimeOffset.UtcNow</c> directly; an architecture
/// test enforces this.
/// </remarks>
public interface IClock
{
    /// <summary>Current instant, always in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Default <see cref="IClock"/> backed by the machine clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
