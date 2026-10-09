# Development Phases

Implemented in order; each phase builds on what the last one shipped. See
[`overview.md`](overview.md) for why Risk and Trading landed early.

## Phase 1 — Infrastructure (delivered)

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

## Phase 2 — Market Data (delivered)

- Binance connection: one reconnecting WebSocket per (symbol, stream kind) —
  the 24hr ticker, trade, kline-per-configured-timeframe and partial depth
  streams — rather than one multiplexed connection, because Binance's partial
  depth payload carries no symbol of its own and the connection it arrived on
  is the only reliable way to attribute it. Exponential backoff with no
  permanent give-up; public market data only, no exchange credentials.
- Normalisation: every message is parsed into a validated domain entity
  (`Tick`, `Trade`, `Candle`, `OrderBookSnapshot`) before anything is
  persisted, cached or published — a malformed or internally inconsistent
  payload (e.g. an OHLC range where the high is not the extreme) is rejected
  at that boundary, the same discipline the Risk Service applies to position
  sizing.
- Staleness handling: a background monitor compares each symbol's most recent
  message age against the configured threshold and publishes
  `market.data.stale` once per transition into staleness, not once per check.
- TimescaleDB writes: raw Npgsql against the Phase 1 hypertables (EF Core
  cannot express a hypertable); trades and candles are written idempotently
  against the schema's own unique indexes, so a WebSocket reconnect replay
  updates or no-ops instead of duplicating.
- Redis latest-value cache: the newest tick, candle and order book per symbol,
  each with a TTL so a stopped ingestion pipeline ages the cache out instead
  of serving an arbitrarily old price forever. Also populates the
  `agentiva:market:tick:{symbol}` key the Trading Service's
  `RedisMarketConditionProvider` already reads, which is what makes the Risk
  Service's market-data-staleness check bind against a real feed for the
  first time.
- `market.*` events: `market.tick.created`, `market.trade.created`,
  `market.candle.created` (closed bars only), `market.orderbook.updated` and
  `market.data.stale`, published directly rather than through the
  transactional outbox — there is no relational write in the same transaction
  for an outbox to be atomic with, and market data is not a financial command.
- A small read API (`GET /api/v1/market/symbols/{symbol}/ticker`, `/candles`,
  `/orderbook`, `/indicators` and `/status` for live feed health) behind the
  gateway's existing `market-read`/`market-write` routes — the same surface
  the AI platform's `get_market_data`, `get_candles`, `get_orderbook` and
  `get_indicators` tools call. The `/indicators` route reads
  `indicator_snapshots`, written by the Strategy Service in Phase 3 — see
  below.

## Phase 3 — Strategy (delivered)

- Indicator engine (`Agentiva.Strategy.Domain.Indicators`): EMA, Wilder's
  RSI, MACD (with its own signal line), Wilder's ATR and a window-scoped
  VWAP, each a pure function returning `null` rather than an approximate
  value when the series is too short — see that type's own remarks on why an
  honest "not enough data" matters more here than it would look to.
- Three deterministic strategies, each implementing a shared `IStrategy`
  interface: EMA(12/26)-crossover filtered by RSI(14); EMA50/EMA200
  trend-following with an extreme-RSI filter; a 20-bar Donchian-channel
  breakout. Every stop-loss and take-profit is sized off ATR rather than a
  fixed price distance, so it widens and narrows with the symbol's own
  recently observed volatility.
- Strategy versioning: every signal is attributed to a `StrategyDefinition`
  row's identity and current semantic version — seeded and reconciled by
  `StrategySeeder` — so a signal can always be traced back to exactly the
  logic that produced it even after the code moves on to a later version.
- Ingestion: consumes `market.candle.created` (the Strategy Service's first
  real event *consumer*, exercising the inbox/retry/dead-letter machinery
  Phase 1 built but nothing had yet used), maintains an in-memory rolling
  window per symbol and timeframe, computes and persists an indicator
  snapshot on every closed candle, and evaluates every active strategy
  against it.
- Every evaluation is recorded — including a hold, which the platform's own
  `TradeAction` enum already documented as "modelled so that a decision to
  stand aside is auditable" — but `signal.created` is published only for an
  actionable buy or sell, through the transactional outbox this time: unlike
  Phase 2's direct publish, a signal genuinely is a relational write (the
  `Signal` row) that the announcement of it must stay atomic with.
- A small read API: `GET /api/v1/strategies/performance`, `/{id}/performance`
  and `/signals` — the surface the AI platform's `get_strategy_performance`
  and `get_recent_signals` tools call. Performance is signal counts only, by
  design — see known-limitations.md for why a win rate would be fabricated
  at this phase.

## Phase 4 — Risk (limits refinement) (delivered)

- Risk policy CRUD: `POST`/`PUT /policies`, `POST /policies/{id}/deactivate`
  and `/mark-default`, all administrator-only. The structural checks sit in
  FluentValidation; the cross-field invariants (risk-per-trade against daily
  loss, concentration against exposure) live in exactly one place —
  `RiskPolicy.Create`/`UpdateLimits` — so create and update can never drift
  apart on what "internally consistent" means.
- Kill-switch operator endpoints: `GET`/`POST /kill-switch/engage`/`/release`.
  Engaging and releasing both write the Redis flag `PlatformStateProvider`
  already read, and publish `KillSwitchActivated`/`KillSwitchDeactivated`
  directly (no relational write of its own to be atomic with). Configuration
  still wins when it forces the switch engaged — a release while
  `Trading:KillSwitchEnabled=true` clears the flag but reports the switch
  still functionally engaged, rather than claiming a release that did not
  take effect.
- The real market-condition provider: the Strategy Service's indicator
  engine now feeds an ATR-derived annualised volatility estimate into
  `RedisMarketConditionProvider` through its own Redis key, closing the
  Phase 2/3 gap where `VolatilityPercent` was always null. Two independently
  written keys, not one shared key, because a plain Redis `SET` replaces a
  value wholesale and two writers sharing one key would race on every
  update.
- The portfolio provider: an operator-adjustable paper ledger
  (`GET`/`PUT /trading/paper-ledger`, `POST /trading/paper-ledger/reset`)
  replaces the Phase 1 hardcoded baseline. Deliberately not a real portfolio
  — nothing tracks fills or derives exposure from actual trades, and
  per-symbol exposure still always reports zero. Genuine portfolio truth
  needs real fills (Execution Service, Phase 5) and real position tracking
  (Portfolio Service, Phase 6); this is a Redis-backed, fail-safe-default
  value an operator can move in the meantime, the same shape as the kill
  switch.

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
