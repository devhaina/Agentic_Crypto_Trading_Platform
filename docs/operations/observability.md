# Observability

## Why metrics go through a Collector instead of each service exposing `/metrics`

`OpenTelemetry.Exporter.Prometheus.AspNetCore` has only ever shipped as a
beta package for .NET. Rather than put a beta dependency on the critical path
of a financial system, every service exports metrics and traces over OTLP to
the OpenTelemetry Collector (`infrastructure/monitoring/otel-collector/`),
which exposes a stable Prometheus-format endpoint that Prometheus scrapes.
One side benefit: sampling, batching, and resource-attribute handling are
configured once in the collector instead of in fourteen services.

## Correlation end to end

`CorrelationMiddleware` (HTTP) and `RabbitMqConsumerService` (messaging) both
populate `ICorrelationContext`, which `LoggingBehavior` stamps onto every log
line and `RabbitMqEventPublisher` stamps onto every outgoing message header.
One `X-Correlation-Id`, set by the Angular client's
`correlation.interceptor.ts` or minted at the gateway, follows a single user
action through every service and queue hop it touches. `TraceContextEnricher`
additionally stamps the OpenTelemetry `TraceId`/`SpanId` onto every log event,
which is what makes the "jump from a Loki line to its Tempo/Jaeger trace"
workflow in Grafana's Loki data source (`derivedFields`) work.

## Local retention

Loki is configured for 168h (7 days) local retention — appropriate for a
development stack, not for compliance. Production retention for audit-
adjacent logs is a separate decision from Loki's retention entirely: the
durable, long-retention audit record is `audit_db` (Phase 5), not the log
store.

## Production trace backend

The collector's trace pipeline currently exports to `debug` (console) only.
Adding a production backend (Tempo, Jaeger, an APM vendor) means adding one
exporter to `infrastructure/monitoring/otel-collector/config.yaml`; no
service changes, because every service already speaks OTLP to the collector
and knows nothing about where traces end up after that.
