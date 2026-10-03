using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Risk.Application.Abstractions;
using Agentiva.Risk.Domain.Entities;
using Agentiva.Risk.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Agentiva.Risk.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="IRiskPolicyRepository"/>.</summary>
public sealed class RiskPolicyRepository(RiskDbContext context) : IRiskPolicyRepository
{
    public Task<RiskPolicy?> GetDefaultAsync(CancellationToken cancellationToken)
        => context.RiskPolicies
            .FirstOrDefaultAsync(p => p.IsDefault && p.IsActive, cancellationToken);

    public Task<RiskPolicy?> GetByIdAsync(RiskPolicyId id, CancellationToken cancellationToken)
        => context.RiskPolicies
            .FirstOrDefaultAsync(p => p.Id == id && p.IsActive, cancellationToken);

    public async Task<IReadOnlyList<RiskPolicy>> ListAsync(CancellationToken cancellationToken)
        => await context.RiskPolicies
            .AsNoTracking()
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

    public void Add(RiskPolicy policy) => context.RiskPolicies.Add(policy);
}

/// <summary>EF Core implementation of <see cref="IRiskCheckRepository"/>.</summary>
public sealed class RiskCheckRepository(RiskDbContext context) : IRiskCheckRepository
{
    public void Add(RiskCheckRecord record) => context.RiskChecks.Add(record);

    public Task<RiskCheckRecord?> GetByIdAsync(RiskCheckId id, CancellationToken cancellationToken)
        => context.RiskChecks.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<RiskCheckRecord>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken)
        => await context.RiskChecks
            .AsNoTracking()
            .OrderByDescending(r => r.EvaluatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);
}
