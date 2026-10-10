# Agentiva — Agentic Crypto Trading Platform

A production-shaped, microservice-based crypto trading platform where AI agents
analyze markets and propose trades, a deterministic risk service is the only
gate that can approve them, and a separate execution service is the only
component that ever talks to an exchange.

```
AI Agents → Trading Proposal → Trading Service → Risk Service → APPROVED → Execution Service → Exchange
```

**The platform does not promise profit.** AI agents analyze and propose; they
never execute. See [`docs/architecture/overview.md`](docs/architecture/overview.md)
for the full security boundary.

## Status: Phase 6 — Portfolio

This is the sixth deliverable in an eleven-phase plan (see
[`docs/architecture/phases.md`](docs/architecture/phases.md)). Phases 1–6 ship:

- A compiling, runnable microservice skeleton for all 13 .NET services, the AI
  agent platform, and the Angular dashboard.
- **Full working logic** in six services: **Risk Service** (deterministic
  position sizing, the full risk-check gate, policy CRUD, the kill switch),
  **Trading Service** (the intent → risk → execution workflow), **Market
  Data Service** (Binance WebSocket ingestion, normalisation, staleness
  detection), **Strategy Service** (the indicator engine and three
  deterministic strategies), **Execution Service** (a signed Binance REST
  client gated by trading mode, the order state machine, and the real
  duplicate-order check the other services now depend on), and **Portfolio
  Service** (average-cost position tracking, real P&L and exposure built
  from order fills, the real portfolio snapshot the Trading Service's risk
  workflow now reads).
- The complete AI agent platform: five analysis agents, a strategy agent, an
  orchestration graph, a deterministic stub LLM provider (no vendor account
  needed), and the enforced tool-permission boundary.
- Every piece of shared infrastructure: Postgres (database-per-service),
  TimescaleDB, Redis, RabbitMQ (with per-service retry/dead-letter topology),
  OpenTelemetry → Prometheus/Grafana/Loki, Docker Compose, and Kubernetes
  manifests.
- 178 unit tests and 21 architecture tests, plus a separate
  Testcontainers-backed integration suite — see [Testing](#testing) below.

Everything else is a compiling skeleton with a documented "lands in Phase N"
note on its page or endpoint — never a fabricated number or a fake response.

## Quick start

```bash
cp .env.example .env            # generates nothing; edit secrets in, or use the
                                 # openssl one-liners in the file's comments
docker compose up --build
```

Once healthy:

| What | URL |
|---|---|
| Dashboard | http://localhost:4200 |
| API Gateway (Swagger) | http://localhost:8080/swagger |
| Gateway health | http://localhost:8080/health |
| AI agent platform (Swagger) | http://localhost:8000/swagger |
| RabbitMQ management | http://localhost:15672 |
| Grafana | http://localhost:3000 |
| Prometheus | http://localhost:9090 |

Every service also exposes `/alive` (liveness), `/ready` (readiness) and
`/health` (detailed) directly.

**No exchange connection is made in Phase 1.** `TRADING__MODE` defaults to
`PAPER`, and switching to `LIVE` additionally requires `TRADING__ALLOWLIVE=true`
— two independent settings, so one typo cannot enable real trading.

## Try the risk gate and the AI pipeline

```bash
# Ask the agent platform to analyse a symbol (runs on the deterministic stub
# provider by default — no LLM API key needed):
curl -s -X POST http://localhost:8000/api/v1/agents/runs \
  -H 'Content-Type: application/json' \
  -d '{"symbol": "BTCUSDT", "timeframe": "15m"}' | jq

# Submit a trading intent directly to the Trading Service (bypassing the
# gateway's auth for a Phase 1 smoke test — see docs/api for the real flow):
curl -s -X POST http://localhost:8083/api/v1/trading/intents \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: demo-001' \
  -d '{
    "tradingAccountId": "00000000-0000-0000-0000-000000000001",
    "symbol": "BTCUSDT", "side": "BUY", "quantity": "0.01",
    "entryPrice": "100000", "stopLoss": "98000", "takeProfit": "104000",
    "confidence": "0.75", "source": "MANUAL"
  }' | jq
```

The intent is recorded, submitted to the Risk Service, and the response shows
the real decision: `RiskApproved` with a sized quantity, or `RiskRejected` with
every failing check. Nothing here is simulated.

## Repository layout

```
services/            13 .NET microservices (Clean Architecture where logic exists)
src/building-blocks/ Shared .NET libraries: Domain, Application, Persistence,
                      Messaging, Observability, ServiceDefaults
contracts/events/    Published RabbitMQ event contracts — the only thing
                      services may share across boundaries
ai/agent-platform/    Python/FastAPI AI agent platform
frontend/             Angular 22 dashboard
infrastructure/       Docker, Kubernetes, Postgres/Timescale init, RabbitMQ,
                      Prometheus/Grafana/Loki/OTel config
tests/                Unit, architecture, and integration test projects
docs/                 Architecture, security, database, operations, runbooks
```

## Architectural rules this repository enforces, not just states

- **No floating point for money.** Enforced by an architecture test that scans
  every financial type for `float`/`double`, and by a global EF Core
  convention mapping every `decimal` to `NUMERIC(38,18)`.
- **The AI cannot move funds.** Enforced three ways: the Python tool registry
  refuses to register a forbidden tool name (including near-misses like
  `submit_spot_order`) at startup; a `TradingProposal` has no field that could
  express a position size; and in Kubernetes, a `NetworkPolicy` denies the
  agent-platform pod egress to anything but the gateway — so even a fully
  compromised agent pod cannot reach an exchange.
- **Every financial command is idempotent.** A shared `IdempotencyBehavior`
  claims a caller-supplied key via a uniquely-indexed Postgres row before a
  handler runs; a second delivery of the same key replays the first outcome
  instead of executing twice. Verified under concurrent load in
  `Agentiva.IntegrationTests`.
- **Clean Architecture is a build-time gate.** `Agentiva.ArchitectureTests`
  asserts the Risk and Trading domains have zero dependency on EF Core,
  ASP.NET Core, RabbitMQ or each other's assemblies.
- **No service other than Execution references exchange credentials.** Checked
  by a source-text scan in CI and by Kubernetes RBAC scoping the credential
  secret to one `ServiceAccount`.

## Testing

```bash
dotnet test tests/Agentiva.UnitTests           # 77 tests — value objects, mediator
dotnet test tests/Agentiva.ArchitectureTests    # 13 tests — layering, financial safety
dotnet test tests/Agentiva.IntegrationTests     # Testcontainers: real Postgres

cd ai/agent-platform && .venv/bin/python -m pytest  # 84 tests — AI trust boundary,
                                                      # proposal contract, pipeline
```

The integration tests start real PostgreSQL containers via Testcontainers,
because the behaviour under test — `FOR UPDATE SKIP LOCKED` in the outbox
claim, the unique-index idempotency race — does not exist in an in-memory
provider.

## Known limitations

Documented in full at
[`docs/architecture/known-limitations.md`](docs/architecture/known-limitations.md).
In brief: the Trading Service's old operator-adjustable paper ledger is gone
— the Portfolio Service (Phase 6) now derives equity, cash, exposure and
P&L from real order fills, with average-cost position tracking and a real
per-symbol exposure figure the paper ledger could never report; an
unreachable Portfolio Service fails the intent closed rather than feeding
the risk gate a fabricated snapshot; account-level `PortfolioUpdated`/
`PnlUpdated` events are not yet published, only position/balance/trade
ones, and nothing yet traces a signal to the trade it produced for a win
rate. The Risk Service's duplicate-order check is real as of Phase 5 (it
calls the Execution Service's own order store) but fails closed rather than
retried when that service is unreachable; the Market Data Service (Phase 2)
connects to Binance's public streams, a separate path from the Execution
Service (Phase 5), which is the only service that holds trading credentials
and, only in `Live` mode, places a real order; an order left resting on the
exchange book has no consumer yet to advance its intent asynchronously; the
Strategy Service's (Phase 3) candle buffer warms up from nothing on every
restart; the kill switch (Phase 4) has no durable record beyond its own
published event, and engaging it does not yet cancel resting orders; every
position and intent on the dashboard is read against one fixed,
caller-supplied trading account id, since there is no Identity Service or
account-selection UI yet — see known-limitations.md for why.

## Documentation

- [`docs/architecture/overview.md`](docs/architecture/overview.md) — the security boundary and service map
- [`docs/architecture/phases.md`](docs/architecture/phases.md) — the eleven-phase plan and what Phase 1 shipped
- [`docs/architecture/known-limitations.md`](docs/architecture/known-limitations.md)
- [`docs/database/schema.md`](docs/database/schema.md) — database-per-service layout
- [`docs/security/`](docs/security/) — identity, database access, the AI trust boundary
- [`docs/operations/`](docs/operations/) — migrations, alerting, going live
- [`docs/runbooks/`](docs/runbooks/) — reconciliation failure, indeterminate orders
- [`docs/sdlc/`](docs/sdlc/) — a separate, pre-existing high-level planning pack (PRD/BRD/SRS-style); not maintained as part of this implementation

## License

Proprietary. Not for external distribution.
