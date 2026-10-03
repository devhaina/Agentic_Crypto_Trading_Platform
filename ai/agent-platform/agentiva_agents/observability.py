"""Structured logging and OpenTelemetry setup.

Logs are JSON on stdout, matching the .NET services, so one Loki query spans the
whole platform rather than needing a different parser per language.
"""

from __future__ import annotations

import logging
import sys
from typing import Any

import structlog

from agentiva_agents.config import Settings


def configure_logging(settings: Settings) -> None:
    """Configures structlog to emit JSON on stdout."""
    logging.basicConfig(
        format="%(message)s",
        stream=sys.stdout,
        level=getattr(logging, settings.log_level),
    )

    # Quieten the access log: the health endpoint is polled constantly and would
    # otherwise dominate the log volume.
    logging.getLogger("uvicorn.access").setLevel(logging.WARNING)

    structlog.configure(
        processors=[
            structlog.contextvars.merge_contextvars,
            structlog.processors.add_log_level,
            structlog.processors.TimeStamper(fmt="iso", utc=True),
            structlog.processors.StackInfoRenderer(),
            structlog.processors.format_exc_info,
            _add_service_context(settings),
            structlog.processors.JSONRenderer(),
        ],
        wrapper_class=structlog.make_filtering_bound_logger(
            getattr(logging, settings.log_level)
        ),
        logger_factory=structlog.PrintLoggerFactory(file=sys.stdout),
        cache_logger_on_first_use=True,
    )


def _add_service_context(settings: Settings) -> Any:
    """Returns a processor stamping the service name on every event."""

    def processor(
        _logger: Any, _method: str, event_dict: dict[str, Any]
    ) -> dict[str, Any]:
        event_dict["service.name"] = settings.service_name
        event_dict["deployment.environment"] = settings.environment
        return event_dict

    return processor


def configure_telemetry(settings: Settings, app: Any) -> None:
    """Configures OpenTelemetry tracing and instruments FastAPI.

    Exports over OTLP to the collector, the same path the .NET services use, so
    a request that crosses from the gateway into an agent run appears as one
    trace rather than two.
    """
    if not settings.otlp_endpoint:
        return

    try:
        from opentelemetry import trace
        from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
        from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
        from opentelemetry.instrumentation.httpx import HTTPXClientInstrumentor
        from opentelemetry.sdk.resources import Resource
        from opentelemetry.sdk.trace import TracerProvider
        from opentelemetry.sdk.trace.export import BatchSpanProcessor
    except ImportError:  # pragma: no cover - depends on optional extras
        structlog.get_logger(__name__).warning(
            "telemetry_unavailable", reason="OpenTelemetry packages are not installed"
        )
        return

    resource = Resource.create(
        {
            "service.name": settings.service_name,
            "service.version": "0.1.0",
            "deployment.environment": settings.environment,
        }
    )

    provider = TracerProvider(resource=resource)
    provider.add_span_processor(
        BatchSpanProcessor(OTLPSpanExporter(endpoint=settings.otlp_endpoint, insecure=True))
    )
    trace.set_tracer_provider(provider)

    # Health probes excluded: they fire constantly and carry no diagnostic value.
    FastAPIInstrumentor.instrument_app(app, excluded_urls="health,ready,alive,metrics")
    HTTPXClientInstrumentor().instrument()

    structlog.get_logger(__name__).info(
        "telemetry_configured", endpoint=settings.otlp_endpoint
    )
