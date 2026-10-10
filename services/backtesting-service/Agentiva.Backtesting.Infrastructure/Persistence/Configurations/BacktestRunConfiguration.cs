using System.Text.Json;
using Agentiva.Backtesting.Domain.Metrics;
using Agentiva.Backtesting.Domain.Runs;
using Agentiva.BuildingBlocks.Common.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Agentiva.Backtesting.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for <see cref="BacktestRun"/>.</summary>
/// <remarks>
/// <see cref="BacktestRun.Metrics"/> and <see cref="BacktestRun.WalkForwardWindows"/>
/// are read-model value objects — assigned exactly once by
/// <see cref="BacktestRun.Complete"/>, never queried by their own fields —
/// so they are stored as a single <c>jsonb</c> column each rather than
/// mapped relationally, the same reasoning <c>OutboxMessage.Payload</c>
/// already applies to an opaque result blob. <see cref="AgentivaJson"/>
/// writes every decimal as a JSON string, so this loses no precision the
/// way a naive <c>System.Text.Json</c> default would.
/// </remarks>
public sealed class BacktestRunConfiguration : IEntityTypeConfiguration<BacktestRun>
{
    public void Configure(EntityTypeBuilder<BacktestRun> builder)
    {
        builder.ToTable("backtest_runs", "backtesting");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Timeframe).HasMaxLength(8).IsRequired();
        builder.Property(r => r.StrategyName).HasMaxLength(50).IsRequired();
        builder.Property(r => r.StrategyVersion).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.FailureReason).HasMaxLength(2000);

        builder
            .Property(r => r.Metrics)
            .HasConversion(
                metrics => metrics == null ? null : JsonSerializer.Serialize(metrics, AgentivaJson.Options),
                json => json == null ? null : JsonSerializer.Deserialize<PerformanceMetrics>(json, AgentivaJson.Options))
            .HasColumnType("jsonb");

        builder
            .Property(r => r.WalkForwardWindows)
            .HasConversion(
                new ValueConverter<IReadOnlyList<WalkForwardWindow>, string>(
                    windows => JsonSerializer.Serialize(windows, AgentivaJson.Options),
                    json => JsonSerializer.Deserialize<List<WalkForwardWindow>>(json, AgentivaJson.Options)
                        ?? new List<WalkForwardWindow>()),
                new ValueComparer<IReadOnlyList<WalkForwardWindow>>(
                    (a, b) => (a ?? new List<WalkForwardWindow>()).SequenceEqual(b ?? new List<WalkForwardWindow>()),
                    windows => windows.Aggregate(0, (hash, w) => HashCode.Combine(hash, w.GetHashCode())),
                    windows => windows.ToList()))
            .HasColumnType("jsonb");

        // The dashboard's recent-runs list, newest first — see ListAsync.
        builder.HasIndex(r => r.CreatedAt).HasDatabaseName("ix_backtest_runs_created_at");

        builder
            .HasMany(r => r.Trades)
            .WithOne()
            .HasForeignKey(t => t.BacktestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
