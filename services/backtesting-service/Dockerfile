#
# Agentiva — Backtesting Service
#
# Build from the REPOSITORY ROOT, never from this directory:
#
#   docker build -f services/backtesting-service/Dockerfile -t agentiva/backtesting-service:dev .
#
# The context must be the repository root because this service references the
# shared building blocks and the published event contracts by relative path.

ARG DOTNET_VERSION=10.0

# -----------------------------------------------------------------------------
# Stage 1 — build and publish
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS publish

ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Central package management. Copied first and on their own: they change far
# less often than source, so this layer — and the restore that depends on it —
# survives most edits.
COPY global.json Directory.Build.props Directory.Packages.props ./

COPY src/building-blocks/ ./src/building-blocks/
COPY contracts/ ./contracts/
COPY services/backtesting-service/ ./services/backtesting-service/

RUN dotnet restore "services/backtesting-service/Agentiva.Backtesting.Api/Agentiva.Backtesting.Api.csproj"

RUN dotnet publish "services/backtesting-service/Agentiva.Backtesting.Api/Agentiva.Backtesting.Api.csproj" \
      --configuration "$BUILD_CONFIGURATION" \
      --no-restore \
      --output /app/publish \
      /p:UseAppHost=false

# -----------------------------------------------------------------------------
# Stage 2 — runtime
#
# The aspnet image, not the SDK: around a fifth of the size, and it ships no
# compiler, package manager or build tooling, so a compromised container has
# very little to work with.
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime

# curl is installed solely for the container health check. The aspnet image
# ships neither curl nor wget, and a health check is not optional for a service
# an orchestrator has to decide whether to route traffic to.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1 \
    DOTNET_EnableDiagnostics=0 \
    TZ=UTC

COPY --from=publish /app/publish ./

# Runs as the image's pre-created non-root user. Nothing this service does
# requires root, and a container escape from root is a host compromise.
USER $APP_UID

EXPOSE 8080

# Probes /alive, which is process-local and touches no dependency. Probing
# /ready here would make the container be killed and restarted during a
# database blip, turning a recoverable outage into a crash loop.
HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=3 \
    CMD curl --fail --silent --show-error http://localhost:8080/alive || exit 1

# Exec form, so dotnet is PID 1 and receives SIGTERM directly. Wrapping this in
# a shell would make the shell PID 1, which does not forward signals — the
# runtime would never run its shutdown handlers, and in-flight message handlers
# would be killed mid-work and redelivered instead of finishing cleanly.
ENTRYPOINT ["dotnet", "Agentiva.Backtesting.Api.dll"]
