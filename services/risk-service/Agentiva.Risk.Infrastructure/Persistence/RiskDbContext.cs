using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Persistence.Abstractions;
using Agentiva.Risk.Domain.Entities;
using Agentiva.Risk.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Agentiva.Risk.Infrastructure.Persistence;

/// <summary>
/// The Risk Service's database. Owns <c>risk_db</c> and is queried by no other service.
/// </summary>
/// <remarks>
/// Risk policies live here rather than in the Configuration Service, and that
/// separation is deliberate: the limits that constrain the platform must not be
/// editable through the same path as the behaviour they constrain. A change here
/// is an audited, administrator-only operation against this service's own API.
/// </remarks>
public sealed class RiskDbContext(
    DbContextOptions<RiskDbContext> options,
    IClock clock,
    ICorrelationContext correlation)
    : AgentivaDbContext(options, clock, correlation)
{
    protected override string Schema => "risk";

    /// <summary>Configured risk policies.</summary>
    public DbSet<RiskPolicy> RiskPolicies => Set<RiskPolicy>();

    /// <summary>Append-only evaluation records, approvals and rejections alike.</summary>
    public DbSet<RiskCheckRecord> RiskChecks => Set<RiskCheckRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyConfigurationsFrom(modelBuilder, typeof(RiskDbContext).Assembly);
    }
}
