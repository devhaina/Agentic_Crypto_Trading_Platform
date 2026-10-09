using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Strategy.Domain.Entities;

namespace Agentiva.Strategy.Application.Abstractions;

/// <summary>Persistence for <see cref="StrategyDefinition"/>, satisfied by Infrastructure.</summary>
public interface IStrategyDefinitionRepository
{
    void Add(StrategyDefinition strategy);

    Task<StrategyDefinition?> GetByNameAsync(string name, CancellationToken cancellationToken);

    Task<StrategyDefinition?> GetByIdAsync(StrategyId id, CancellationToken cancellationToken);

    /// <summary>Every strategy currently evaluated by the ingestion pipeline.</summary>
    Task<IReadOnlyList<StrategyDefinition>> ListActiveAsync(CancellationToken cancellationToken);

    /// <summary>Every strategy, active or not — used by the performance endpoints.</summary>
    Task<IReadOnlyList<StrategyDefinition>> ListAllAsync(CancellationToken cancellationToken);
}
