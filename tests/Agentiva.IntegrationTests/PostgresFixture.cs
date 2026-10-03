using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.Risk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Agentiva.IntegrationTests;

/// <summary>
/// A real PostgreSQL instance for the duration of the test class.
/// </summary>
/// <remarks>
/// <para>
/// A real database rather than the in-memory provider, deliberately. The
/// behaviour these tests verify does not exist in the in-memory provider at
/// all: <c>FOR UPDATE SKIP LOCKED</c> in the outbox claim, the unique-index
/// race that makes idempotency safe, <c>jsonb</c> columns, partial indexes, and
/// <c>NUMERIC(38,18)</c> precision. A passing in-memory test would prove
/// nothing about any of them.
/// </para>
/// <para>
/// The container is shared across a class and disposed afterwards; each test
/// gets a fresh schema.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        // Pinned to the same major version the platform runs in production. A
        // "latest" tag would make the suite's behaviour change without a commit.
        .WithImage("postgres:17-alpine")
        .WithDatabase("risk_test")
        .WithUsername("agentiva")
        .WithPassword("integration-test-only")
        .WithCleanUp(true)
        .Build();

    /// <summary>Connection string for the running container.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates a context against the container and applies migrations.</summary>
    public async Task<RiskDbContext> CreateContextAsync(
        IClock? clock = null,
        ICorrelationContext? correlation = null)
    {
        var options = new DbContextOptionsBuilder<RiskDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        var context = new RiskDbContext(
            options,
            clock ?? new SystemClock(),
            correlation ?? new CorrelationContext
            {
                CorrelationId = "integration-test",
                RequestId = "integration-test",
            });

        // The real migrations, not EnsureCreated. This verifies that the
        // committed migration actually applies — which is a thing that breaks,
        // and breaks in a way no unit test would catch.
        await context.Database.MigrateAsync();

        return context;
    }
}

/// <summary>A fixed clock, so time-dependent behaviour is assertable.</summary>
public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
