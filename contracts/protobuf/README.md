# Protobuf Contracts

Reserved for gRPC contracts between services that need a synchronous,
immediate-response call where REST's overhead matters (Rule, §3: "Communicate
synchronously through REST/gRPC only when immediate responses are required").

Phase 1 has exactly one synchronous cross-service call — Trading Service to
Risk Service — and it uses REST (`RiskServiceClient` in
`Agentiva.Trading.Infrastructure.Clients`), because the call volume is low and
a human-readable JSON payload is worth more during this phase than gRPC's
lower latency. This directory stays empty until a specific, measured need for
gRPC appears; it is not populated speculatively.
