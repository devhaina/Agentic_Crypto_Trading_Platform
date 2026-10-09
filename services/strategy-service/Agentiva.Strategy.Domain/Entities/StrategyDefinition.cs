using Agentiva.BuildingBlocks.Domain.Abstractions;
using Agentiva.BuildingBlocks.Domain.Exceptions;
using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.Strategy.Domain.Entities;

/// <summary>
/// Identity and current version of one deterministic strategy.
/// </summary>
/// <remarks>
/// The decision logic itself lives entirely in code — see
/// <c>Agentiva.Strategy.Domain.Strategies</c> — not in this row. What this
/// row owns is the stable identity a signal is attributed to and which
/// version of that logic was active when the signal was produced, so a
/// signal can always be traced back to exactly the rules that produced it
/// even after the code moves on to a later version.
/// </remarks>
public sealed class StrategyDefinition : AggregateRoot<StrategyId>
{
    private StrategyDefinition()
    {
    }

    private StrategyDefinition(
        StrategyId id, string name, string description, string currentVersion, bool isActive, DateTimeOffset createdAt)
        : base(id)
    {
        Name = name;
        Description = description;
        CurrentVersion = currentVersion;
        IsActive = isActive;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>Stable strategy name, e.g. <c>EMA_RSI</c>. Matches <see cref="Strategies.IStrategy.Name"/>.</summary>
    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>Semantic version of the currently active logic, e.g. <c>1.0.0</c>.</summary>
    public string CurrentVersion { get; private set; } = string.Empty;

    /// <summary>Whether the engine evaluates this strategy on every closed candle.</summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <exception cref="DomainException">The name, description or version is empty.</exception>
    public static StrategyDefinition Create(string name, string description, string version, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("domain.strategy.empty_name", "A strategy must have a name.");
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new DomainException("domain.strategy.empty_version", "A strategy must have a version.");
        }

        return new StrategyDefinition(StrategyId.New(), name.Trim(), description.Trim(), version.Trim(), true, now);
    }

    /// <summary>Records that the running code's version has moved on from what this row last recorded.</summary>
    public void UpdateVersion(string version, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new DomainException("domain.strategy.empty_version", "A strategy must have a version.");
        }

        CurrentVersion = version.Trim();
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }
}
