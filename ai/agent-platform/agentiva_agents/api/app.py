"""The Agentiva AI agent platform HTTP API.

Exposes analysis and proposal endpoints. There is no endpoint here that can
place an order, change a risk policy or touch an exchange — the platform holds
no credential that would make one possible.
"""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Annotated, Any

import structlog
from fastapi import Depends, FastAPI, Header, HTTPException, Request, status
from fastapi.responses import JSONResponse

from agentiva_agents.api.dependencies import PlatformContext, build_platform, build_research_agent
from agentiva_agents.agents.base import AgentExecutionError
from agentiva_agents.config import Settings, get_settings
from agentiva_agents.models.proposals import AgentRunRequest, AgentRunResult
from agentiva_agents.observability import configure_logging, configure_telemetry
from agentiva_agents.policies.permissions import (
    FORBIDDEN_TOOL_NAMES,
    READ_ONLY_TOOL_NAMES,
)

logger = structlog.get_logger(__name__)


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncIterator[None]:
    """Builds the platform at startup and releases it at shutdown."""
    settings = get_settings()
    configure_logging(settings)

    logger.info(
        "agent_platform_starting",
        environment=settings.environment,
        provider=settings.llm_provider,
        stub_mode=settings.is_stub_mode,
    )

    context = build_platform(settings)
    app.state.platform = context
    app.state.research_agent = build_research_agent(context)

    configure_telemetry(settings, app)

    yield

    await context.aclose()
    logger.info("agent_platform_stopped")


def get_platform(request: Request) -> PlatformContext:
    """Returns the platform context built at startup."""
    platform: PlatformContext = request.app.state.platform
    return platform


def create_app() -> FastAPI:
    """Builds the FastAPI application."""
    settings = get_settings()

    app = FastAPI(
        title="Agentiva — AI Agent Platform",
        version="0.1.0",
        description=(
            "Read-only market analysis and trading proposals.\n\n"
            "**Security boundary.** This service holds no exchange credentials and has no "
            "execution tools. Its only output into the trading path is a *proposal*, which the "
            "Trading Service records as an intent and the deterministic Risk Service then "
            "approves or rejects. A proposal carries no position size: sizing is derived solely "
            "by the Risk Service from the portfolio's risk budget.\n\n"
            "No endpoint here can place an order, cancel one, move funds or alter a risk policy."
        ),
        lifespan=lifespan,
        docs_url="/swagger",
        openapi_url="/openapi/v1.json",
    )

    _register_routes(app, settings)
    return app


def _register_routes(app: FastAPI, settings: Settings) -> None:
    """Maps the platform's endpoints."""

    # --- Health ----------------------------------------------------------------
    #
    # Liveness and readiness are separated for the same reason as in the .NET
    # services: a liveness probe that checks a dependency causes the orchestrator
    # to restart every replica during a dependency outage, turning a recoverable
    # blip into an outage.

    @app.get("/alive", tags=["Platform"], summary="Liveness: the process is running.")
    async def alive() -> dict[str, str]:
        return {"status": "Healthy"}

    @app.get("/ready", tags=["Platform"], summary="Readiness: the platform can serve traffic.")
    async def ready(platform: Annotated[PlatformContext, Depends(get_platform)]) -> dict[str, Any]:
        # Ready once the tool registry is sealed and a provider exists. The
        # gateway is not probed: agents degrade to partial evidence when a data
        # source is unavailable, so an unreachable gateway does not make this
        # service unready.
        return {
            "status": "Healthy",
            "provider": platform.provider.model_name,
            "tools": platform.registry.names,
        }

    @app.get("/health", tags=["Platform"], summary="Detailed health and configuration.")
    async def health(
        platform: Annotated[PlatformContext, Depends(get_platform)],
    ) -> dict[str, Any]:
        return {
            "status": "Healthy",
            "service": settings.service_name,
            "version": "0.1.0",
            "environment": settings.environment,
            "checks": [
                {"name": "tool_registry", "status": "Healthy", "toolCount": len(platform.registry.names)},
                {"name": "llm_provider", "status": "Healthy", "model": platform.provider.model_name},
            ],
            "stubMode": settings.is_stub_mode,
        }

    @app.get("/api/v1/info", tags=["Platform"], summary="Describes this service.")
    async def info(
        platform: Annotated[PlatformContext, Depends(get_platform)],
    ) -> dict[str, Any]:
        return {
            "name": settings.service_name,
            "displayName": "AI Agent Platform",
            "version": "0.1.0",
            "description": (
                "Read-only market analysis and trading proposals. Holds no exchange credentials "
                "and has no execution capability."
            ),
            "environment": settings.environment,
            "model": platform.provider.model_name,
            "stubMode": settings.is_stub_mode,
        }

    # --- Trust boundary disclosure -----------------------------------------------

    @app.get(
        "/api/v1/agents/permissions",
        tags=["Agents"],
        summary="Discloses the tools agents may and may not use.",
    )
    async def permissions(
        platform: Annotated[PlatformContext, Depends(get_platform)],
    ) -> dict[str, Any]:
        """Returns the platform's tool permissions.

        Exposed deliberately. An operator auditing the platform should be able
        to confirm the AI trust boundary from a running instance rather than
        from documentation that may have drifted from the code.
        """
        return {
            "registeredTools": platform.registry.names,
            "allowedReadOnlyTools": sorted(READ_ONLY_TOOL_NAMES),
            "forbiddenTools": sorted(FORBIDDEN_TOOL_NAMES),
            "note": (
                "Agents are strictly read-only. Order execution lives in the Execution Service, "
                "which holds the exchange credentials and accepts work only from a risk-approved "
                "trading intent. Registering a forbidden tool raises at startup."
            ),
        }

    @app.get("/api/v1/agents", tags=["Agents"], summary="Lists the configured agents.")
    async def list_agents(
        platform: Annotated[PlatformContext, Depends(get_platform)],
    ) -> dict[str, Any]:
        orchestrator = platform.orchestrator
        return {
            "evidenceAgents": [
                {"name": agent.name, "version": agent.version, "tools": list(agent.required_tools)}
                for agent in orchestrator._evidence_agents  # noqa: SLF001 - introspection endpoint
            ],
            "strategyAgent": {
                "name": orchestrator._strategy_agent.name,  # noqa: SLF001
                "version": orchestrator._strategy_agent.version,  # noqa: SLF001
            },
            "model": platform.provider.model_name,
        }

    # --- The pipeline ---------------------------------------------------------------

    @app.post(
        "/api/v1/agents/runs",
        tags=["Agents"],
        status_code=status.HTTP_200_OK,
        response_model=AgentRunResult,
        summary="Runs the agent pipeline and returns an auditable result.",
    )
    async def create_agent_run(
        body: AgentRunRequest,
        platform: Annotated[PlatformContext, Depends(get_platform)],
        x_correlation_id: Annotated[str | None, Header(alias="X-Correlation-Id")] = None,
    ) -> AgentRunResult:
        """Runs the pipeline for one symbol.

        Returns the complete run record: which agents ran, which tools they
        called, what each concluded, and the resulting proposal if there is one.
        A proposal is advisory and must still pass the deterministic risk gate.
        """
        correlation_id = x_correlation_id or body.correlation_id

        structlog.contextvars.bind_contextvars(correlation_id=correlation_id)

        try:
            return await platform.orchestrator.run(
                symbol=body.symbol,
                timeframe=body.timeframe,
                correlation_id=correlation_id,
            )
        except AgentExecutionError as exc:
            # Validation failure is a 422: the pipeline ran but produced
            # something that did not satisfy the contract, and it was discarded
            # rather than passed on.
            raise HTTPException(
                status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
                detail=(
                    f"The agent pipeline produced output that failed schema validation and was "
                    f"discarded: {exc}"
                ),
            ) from exc
        finally:
            structlog.contextvars.unbind_contextvars("correlation_id")

    @app.post(
        "/api/v1/agents/research",
        tags=["Agents"],
        summary="Runs the research agent against historical performance.",
    )
    async def run_research(
        body: AgentRunRequest,
        request: Request,
    ) -> dict[str, Any]:
        """Runs the research agent.

        Separate from the proposal pipeline and unable to influence it. The
        research agent analyses historical performance and never modifies a
        production strategy — it has no write tool and its output contract has
        no action field.
        """
        agent = request.app.state.research_agent

        try:
            result = await agent.run(body.symbol, body.timeframe)
        except AgentExecutionError as exc:
            raise HTTPException(
                status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
                detail=f"The research agent produced invalid output: {exc}",
            ) from exc

        return {
            "agent": agent.name,
            "version": agent.version,
            "durationMs": result.duration_ms,
            "model": result.model,
            "toolsUsed": result.tools_used,
            "finding": result.output.model_dump(mode="json"),
            "note": (
                "Advisory only. The research agent cannot modify a production strategy; "
                "acting on these findings is a human decision."
            ),
        }

    # --- Error handling -------------------------------------------------------------

    @app.exception_handler(AgentExecutionError)
    async def agent_execution_error_handler(
        request: Request, exc: AgentExecutionError
    ) -> JSONResponse:
        logger.error("agent_execution_error", path=request.url.path, error=str(exc))

        return JSONResponse(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            content={
                "title": "Agent execution failed",
                "detail": str(exc),
                "status": status.HTTP_422_UNPROCESSABLE_ENTITY,
            },
        )


app = create_app()
