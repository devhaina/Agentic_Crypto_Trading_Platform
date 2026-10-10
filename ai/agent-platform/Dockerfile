#
# Agentiva — AI Agent Platform
#
# Build from THIS directory:
#   docker build -t agentiva/agent-platform:dev ai/agent-platform
#
# This image holds no exchange credentials and contains no code that could
# place an order. Its only network dependency is the API gateway's read-only
# routes.

FROM python:3.12-slim AS base

ENV PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    TZ=UTC

# -----------------------------------------------------------------------------
# Stage 1 — dependencies
#
# Installed into a virtualenv so the runtime stage can copy one directory and
# inherit nothing else from the build environment.
# -----------------------------------------------------------------------------
FROM base AS deps

# build-essential is needed for any dependency without a prebuilt wheel for this
# platform. It stays in this stage and never reaches the runtime image, which is
# the point of separating them: a compiler in a production container is a
# liability, not a feature.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends build-essential \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /build

RUN python -m venv /opt/venv
ENV PATH="/opt/venv/bin:$PATH"

# pyproject.toml alone first, so the dependency layer is reused across every
# source change.
COPY pyproject.toml ./
COPY agentiva_agents/__init__.py ./agentiva_agents/__init__.py

RUN pip install --upgrade pip setuptools wheel \
    && pip install ".[anthropic]"

# -----------------------------------------------------------------------------
# Stage 2 — runtime
# -----------------------------------------------------------------------------
FROM base AS runtime

# curl for the container health check only.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --create-home --shell /usr/sbin/nologin --uid 10001 agentiva

COPY --from=deps /opt/venv /opt/venv
ENV PATH="/opt/venv/bin:$PATH"

WORKDIR /app

COPY --chown=agentiva:agentiva agentiva_agents/ ./agentiva_agents/
COPY --chown=agentiva:agentiva pyproject.toml ./

# Non-root, with a nologin shell. Nothing here needs root.
USER agentiva

EXPOSE 8000

# Probes /alive: process-local, touching no dependency. See the note in the
# .NET service Dockerfiles for why a liveness probe must not check dependencies.
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --fail --silent --show-error http://localhost:8000/alive || exit 1

# Exec form, so uvicorn is PID 1 and receives SIGTERM directly for a graceful
# shutdown of in-flight agent runs.
#
# One worker per container by design: scale with replicas rather than with
# in-process workers, so each replica can be scheduled, observed and restarted
# independently.
ENTRYPOINT ["uvicorn", "agentiva_agents.api.app:app", \
            "--host", "0.0.0.0", \
            "--port", "8000", \
            "--workers", "1", \
            "--no-access-log", \
            "--timeout-graceful-shutdown", "25"]
