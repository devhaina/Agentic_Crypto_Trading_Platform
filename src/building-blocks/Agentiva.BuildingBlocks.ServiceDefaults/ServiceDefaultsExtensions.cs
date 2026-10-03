using System.Reflection;
using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Common.Json;
using Agentiva.BuildingBlocks.Observability.Health;
using Agentiva.BuildingBlocks.Observability.Middleware;
using Agentiva.BuildingBlocks.Observability.Tracing;
using Agentiva.BuildingBlocks.Application.Configuration;
using Agentiva.BuildingBlocks.ServiceDefaults.Configuration;
using Agentiva.BuildingBlocks.ServiceDefaults.Errors;
using Agentiva.BuildingBlocks.ServiceDefaults.OpenApi;
using Agentiva.BuildingBlocks.ServiceDefaults.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using StackExchange.Redis;

namespace Agentiva.BuildingBlocks.ServiceDefaults;

/// <summary>
/// One-call setup for every Agentiva ASP.NET Core service.
/// </summary>
/// <remarks>
/// Centralising this is what keeps fourteen services genuinely consistent. When
/// each service wires its own logging, tracing, health checks and error mapping,
/// they drift — and the drift is discovered during an incident, when one
/// service turns out to have no correlation identifiers or a liveness probe that
/// checks the database.
/// </remarks>
public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// Registers logging, telemetry, health checks, error mapping, JSON options,
    /// authentication and resilient HTTP defaults.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="serviceName">Logical service name, e.g. <c>trading-service</c>.</param>
    /// <param name="displayName">Human-readable name for OpenAPI documents.</param>
    /// <param name="description">What this service does.</param>
    public static WebApplicationBuilder AddAgentivaServiceDefaults(
        this WebApplicationBuilder builder,
        string serviceName,
        string displayName,
        string description)
    {
        var configuration = builder.Configuration;
        var environment = builder.Environment;

        // --- Service identity -------------------------------------------------
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.1.0";
        builder.Services.AddSingleton(new ServiceInfo(serviceName, displayName, version, description));

        // --- Logging ----------------------------------------------------------
        builder.Host.UseSerilog((context, _, loggerConfiguration) =>
            Observability.Logging.SerilogConfiguration.Apply(
                loggerConfiguration, serviceName, context.Configuration, context.HostingEnvironment));

        // --- Core abstractions -------------------------------------------------
        builder.Services.AddSingleton<IClock, SystemClock>();

        // Scoped: one correlation per request or per consumed message.
        builder.Services.AddScoped<CorrelationContext>();
        builder.Services.AddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());

        // --- Telemetry ---------------------------------------------------------
        builder.Services.AddAgentivaOpenTelemetry(serviceName, configuration, environment);

        // --- Trading mode -------------------------------------------------------
        builder.Services
            .AddOptions<TradingOptions>()
            .Bind(configuration.GetSection(TradingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // --- Error mapping -------------------------------------------------------
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<AgentivaExceptionHandler>();

        // --- JSON ----------------------------------------------------------------
        // The same options object used for RabbitMQ payloads, so an HTTP
        // response and an event describing the same value serialise identically
        // — decimals as strings in both.
        builder.Services.Configure<JsonOptions>(options =>
        {
            var shared = AgentivaJson.Create();
            options.SerializerOptions.PropertyNamingPolicy = shared.PropertyNamingPolicy;
            options.SerializerOptions.DefaultIgnoreCondition = shared.DefaultIgnoreCondition;
            options.SerializerOptions.NumberHandling = shared.NumberHandling;

            foreach (var converter in shared.Converters)
            {
                options.SerializerOptions.Converters.Add(converter);
            }
        });

        // --- Security --------------------------------------------------------------
        builder.Services.AddAgentivaAuthentication(configuration);

        // --- OpenAPI ----------------------------------------------------------------
        builder.Services.AddAgentivaOpenApi(serviceName, displayName, description, version);

        // --- Health checks -----------------------------------------------------------
        builder.AddAgentivaHealthChecks(serviceName);

        // --- Outbound HTTP ------------------------------------------------------------
        //
        // Resilience is deliberately NOT applied to every client by default.
        //
        // Automatic retry is correct for an idempotent read and dangerous for a
        // financial command: a timed-out risk evaluation or order submission may
        // well have succeeded, so a transparent retry can produce a second
        // approval or a duplicate order. Making retry opt-in means a client that
        // must not retry simply does not call AddAgentivaResilience, rather than
        // having to remove a handler that was added behind its back.
        //
        // See IHttpClientBuilder.AddAgentivaResilience below.

        // --- Graceful shutdown ----------------------------------------------------------
        builder.Services.Configure<HostOptions>(options =>
        {
            // Long enough for in-flight requests and in-flight message handlers
            // to finish and acknowledge. Cutting a handler off mid-way is what
            // produces a redelivery, and redeliveries are the thing the inbox
            // has to clean up after.
            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
            options.ServicesStartConcurrently = false;
            options.ServicesStopConcurrently = false;
        });

        return builder;
    }

    /// <summary>
    /// Registers health checks for the dependencies named in configuration.
    /// </summary>
    /// <remarks>
    /// Each dependency is probed only when its connection string is present, so
    /// a service that does not use Redis is not reported unhealthy for it.
    /// Dependency checks are tagged <c>ready</c> and never <c>live</c> — see
    /// <see cref="HealthEndpoints"/> for why that distinction matters.
    /// </remarks>
    private static void AddAgentivaHealthChecks(this WebApplicationBuilder builder, string serviceName)
    {
        var configuration = builder.Configuration;
        var checks = builder.Services.AddHealthChecks();

        // Liveness: process-local, no dependencies touched.
        checks.AddCheck(
            "self",
            () => HealthCheckResult.Healthy($"{serviceName} is running."),
            tags: [HealthTags.Live]);

        var postgres = configuration.GetConnectionString("Postgres");
        if (!string.IsNullOrWhiteSpace(postgres))
        {
            checks.AddNpgSql(
                postgres,
                name: "postgres",
                healthQuery: "SELECT 1;",
                tags: [HealthTags.Ready]);
        }

        var timescale = configuration.GetConnectionString("Timescale");
        if (!string.IsNullOrWhiteSpace(timescale))
        {
            checks.AddNpgSql(
                timescale,
                name: "timescaledb",
                healthQuery: "SELECT 1;",
                tags: [HealthTags.Ready]);
        }

        var redis = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
        {
            // One multiplexer for the whole process: it is designed to be
            // shared, and creating one per operation exhausts connections.
            builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var options = ConfigurationOptions.Parse(redis);
                options.AbortOnConnectFail = false;
                options.ConnectRetry = 5;
                options.ConnectTimeout = 5_000;
                return ConnectionMultiplexer.Connect(options);
            });

            checks.AddRedis(
                sp => sp.GetRequiredService<IConnectionMultiplexer>(),
                name: "redis",
                tags: [HealthTags.Ready]);
        }

        // Gated on the connection provider actually being registered, not
        // merely on RabbitMq:Host being configured: that setting is injected
        // into every service uniformly, but only services that call
        // AddAgentivaMessaging register IRabbitMqConnectionProvider. Gating on
        // configuration alone would mark a service permanently Unhealthy for a
        // broker it never connects to.
        var rabbitHost = configuration["RabbitMq:Host"];
        var hasRabbitMqProvider = builder.Services.Any(descriptor =>
            descriptor.ServiceType
                == typeof(Agentiva.BuildingBlocks.Messaging.RabbitMq.IRabbitMqConnectionProvider));
        if (!string.IsNullOrWhiteSpace(rabbitHost) && hasRabbitMqProvider)
        {
            // The factory is required, not optional in practice: left null,
            // this health check falls back to its own default ConnectionFactory
            // (localhost:5672, guest/guest) instead of the credentials and host
            // this service actually connects with — so it would report
            // "Unhealthy" against a correctly running broker. Reusing
            // IRabbitMqConnectionProvider's already-open connection is also
            // cheaper than opening a second one purely to probe it.
            //
            // Bounded to a short timeout rather than calling GetConnectionAsync
            // with no token: that method's retry policy is calibrated for
            // *startup* (RabbitMqOptions defaults to 12 attempts, 5s apart —
            // up to a minute), which is the right patience for a service
            // waiting on a broker that is still starting, and completely the
            // wrong patience for a health check. Called with no token, a /ready
            // probe during that window hangs for up to a minute instead of
            // promptly reporting "not ready yet" — which is worse than useless
            // to an orchestrator deciding whether to route traffic.
            checks.AddRabbitMQ(
                factory: async sp =>
                {
                    var provider = sp
                        .GetRequiredService<Agentiva.BuildingBlocks.Messaging.RabbitMq.IRabbitMqConnectionProvider>();

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    return await provider.GetConnectionAsync(cts.Token);
                },
                name: "rabbitmq",
                tags: [HealthTags.Ready]);
        }
    }

    /// <summary>
    /// Applies the standard middleware pipeline and maps the standard endpoints.
    /// </summary>
    /// <remarks>Call before mapping any service-specific endpoint.</remarks>
    public static WebApplication UseAgentivaServiceDefaults(this WebApplication app)
    {
        var serviceInfo = app.Services.GetRequiredService<ServiceInfo>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Agentiva.Startup");

        // Problem-detail mapping first, so it wraps everything after it.
        app.UseExceptionHandler();

        // Correlation next, so every log line produced downstream — including
        // by the exception handler — carries the identifier.
        app.UseMiddleware<CorrelationMiddleware>();

        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";

            // Health probes would otherwise dominate the request log.
            options.GetLevel = (httpContext, _, exception) =>
            {
                if (exception is not null)
                {
                    return Serilog.Events.LogEventLevel.Error;
                }

                var path = httpContext.Request.Path.Value ?? string.Empty;

                return path.StartsWith("/health", StringComparison.Ordinal)
                       || path.StartsWith("/alive", StringComparison.Ordinal)
                       || path.StartsWith("/ready", StringComparison.Ordinal)
                    ? Serilog.Events.LogEventLevel.Verbose
                    : Serilog.Events.LogEventLevel.Information;
            };
        });

        app.UseAgentivaOpenApi(serviceInfo);

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapAgentivaHealthChecks();

        LogStartupBanner(app, serviceInfo, logger);

        return app;
    }

    /// <summary>
    /// Logs the effective trading mode at startup, loudly if it was downgraded.
    /// </summary>
    private static void LogStartupBanner(WebApplication app, ServiceInfo serviceInfo, Microsoft.Extensions.Logging.ILogger logger)
    {
        var trading = app.Services.GetService<Microsoft.Extensions.Options.IOptions<TradingOptions>>()?.Value;

        if (trading is null)
        {
            return;
        }

        if (trading.IsLiveRequestedButBlocked)
        {
            // Must be impossible to miss: configuration asked for live trading
            // and the platform refused. Silently running in paper mode while an
            // operator believes orders are live would be worse than either.
            logger.LogWarning(
                "{Service} v{Version}: configuration requests LIVE trading but Trading:AllowLive is false. "
                + "Running in {EffectiveMode} mode. No real orders will be placed.",
                serviceInfo.Name,
                serviceInfo.Version,
                trading.EffectiveMode);
        }
        else
        {
            logger.LogInformation(
                "{Service} v{Version} started in {EffectiveMode} trading mode (kill switch: {KillSwitch}).",
                serviceInfo.Name,
                serviceInfo.Version,
                trading.EffectiveMode,
                trading.KillSwitchEnabled ? "ENGAGED" : "clear");
        }
    }
}

/// <summary>Opt-in resilience for outbound HTTP clients.</summary>
public static class HttpResilienceExtensions
{
    /// <summary>
    /// Adds retries, a circuit breaker and timeouts to an HTTP client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Apply this only to clients whose operations are <em>safe to repeat</em> —
    /// reads, and writes guarded by a server-side idempotency key that the
    /// caller is content to replay automatically.
    /// </para>
    /// <para>
    /// Do not apply it to risk evaluation or order submission. Those calls are
    /// ambiguous on timeout: the request may have been processed and only the
    /// response lost, so a retry risks a duplicate financial effect. Those
    /// clients fail closed and leave the retry decision to an operator.
    /// </para>
    /// </remarks>
    public static IHttpClientBuilder AddAgentivaResilience(this IHttpClientBuilder builder)
    {
        builder.AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 3;

            // Jitter stops every replica retrying in lockstep and converting a
            // brief downstream blip into a synchronised thundering herd.
            options.Retry.UseJitter = true;

            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(40);

            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.FailureRatio = 0.5;
            options.CircuitBreaker.MinimumThroughput = 10;
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
        });

        return builder;
    }
}
