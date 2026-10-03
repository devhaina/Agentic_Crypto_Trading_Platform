# Development Phases

Implemented in order; each phase builds on what the last one shipped. See
[`overview.md`](overview.md) for why Risk and Trading landed early.

## Phase 1 — Infrastructure (this deliverable)

- Docker Compose for the full local stack; Kubernetes base + dev/production
  Kustomize overlays.
- PostgreSQL (database-per-service, 12 databases), TimescaleDB (market schema
  with hypertables, provisioned but not yet populated), Redis, RabbitMQ
  (per-service retry/dead-letter topology).
- API Gateway (YARP): routing, JWT auth policies, CORS, two-tier rate
  limiting.
- OpenTelemetry → OTel Collector → Prometheus; Grafana (19-panel dashboard,
  provisioned); Loki; structured JSON logging via Serilog, matched by
  structlog on the Python side.
- **Risk Service: full.** Deterministic policy, position sizer (fees,
  slippage, exchange precision), every check from the specification, full
  audit record.
- **Trading Service: full.** Intent aggregate with enforced state machine,
  the intent→risk workflow, idempotency.
- **AI Agent Platform: full.** Five analysis agents + strategy agent,
  orchestration graph, deterministic stub LLM provider, enforced tool
  permission boundary, FastAPI surface.
- Angular 22 dashboard: shell, live service-health polling, a working
  Dashboard page (real risk policy, real recent intents), a working AI Agents
  page (runs the pipeline, shows the full audit record, discloses tool
  permissions), eleven other pages as honest "lands in Phase N" placeholders.
- CI: build, unit tests, architecture tests, frontend build + audit, Python
  lint/test, secret scanning, container image build + Trivy scan, Kubernetes
  manifest validation.

## Phase 2 — Market Data

Binance WebSocket connection; tick/trade/candle/order-book normalisation;
reconnect and staleness handling; TimescaleDB writes; Redis latest-value
cache; `market.*` events.

## Phase 3 — Strategy

Indicator engine (EMA, RSI, MACD, ATR, VWAP); EMA+RSI, trend-following and
breakout strategies; strategy versioning; `signal.created` events.

## Phase 4 — Risk (limits refinement)

Risk policy CRUD through an authenticated API; kill-switch operator
endpoints; the real portfolio and market-condition providers replacing the
Phase 1 paper baseline.

## Phase 5 — Trading & Execution

Execution Service: Binance REST client, request signing, `IExchangeExecution`
abstraction with a `BinanceExecutionAdapter`; client-order-id idempotency;
order lifecycle events; the real duplicate-order check in the Risk Service.

## Phase 6 — Portfolio

Position and balance tracking from `order.filled`/`trade.completed` events;
real P&L; the real `IPortfolioSnapshotProvider` the Trading Service currently
stubs; reconciliation's first real comparison target.

## Phase 7 — AI (data sources)

Approved news source behind `get_news`; the Anthropic provider exercised
against a real model (the abstraction and the fallback-to-stub logic already
exist); on-chain metrics where available.

## Phase 8 — Backtesting

Simulation engine with explicit fee/slippage modelling; Sharpe, Sortino,
profit factor, max drawdown; walk-forward validation; look-ahead-bias guards.

## Phase 9 — Paper Trading

Run the complete system, Phase 1 through 8, with zero real funds at risk —
the default `TRADING__MODE=PAPER` is already enforced at the execution
boundary today.

## Phase 10 — Dashboard (completion)

The ten remaining placeholder pages filled with real data as their owning
services land: Orders, Positions, Market, Signals, Strategies, Backtesting,
Risk (full policy editor + evaluation history), Audit, Settings,
Administration.

## Phase 11 — Production

Kubernetes rollout via the overlays already committed; CI/CD promotion
pipeline (build → dev → smoke test → manual approval → production, per
`.github/workflows/ci.yml`); External Secrets Operator wiring (the
`ExternalSecret` manifests are written; the vault connection is cluster-
specific); production alerting on the rules already committed in
`infrastructure/monitoring/prometheus/rules/`; disaster recovery drills.
