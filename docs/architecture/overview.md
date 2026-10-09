# Architecture Overview

## The security boundary

```
                    AI Agents (Python/FastAPI)
                    read-only tools, no credentials
                              │
                     TradingProposal (no size field)
                              ▼
                    Trading Service (.NET)
                    records a TradingIntent
                              │
                              ▼
                    Risk Service (.NET)
                    pure, deterministic, no I/O side effects
                              │
                     ┌────────┴────────┐
                   REJECTED          APPROVED (sized quantity)
                     │                  │
                    END                 ▼
                              Execution Service (.NET)
                              ONLY service with exchange credentials
                              ONLY service that calls an exchange
                              │
                              ▼
                           Binance
```

Three independent mechanisms keep an AI agent from moving funds, not one:

1. **No tool exists.** `agentiva_agents/policies/permissions.py` is a
   deny-list, a pattern deny-list (catching `submit_spot_order`,
   `place_order_v2`, and so on), and an allow-list, all three checked at
   startup. Registering a forbidden tool raises `ToolPermissionError` before
   the process finishes starting.
2. **No field exists.** `TradingProposal` (in
   `agentiva_agents/models/proposals.py`) has no `quantity`, `size`,
   `notional`, or `leverage` field. An agent cannot express a position size
   even if every other control vanished, because there is nowhere in the
   contract to put the number. Position size is derived solely by
   `PositionSizer` in the Risk Service from the portfolio's risk budget and
   the proposal's stop distance.
3. **No network path exists** (Kubernetes). `restrict-agent-platform` in
   `infrastructure/kubernetes/base/networkpolicies.yaml` denies the
   agent-platform pod all egress except to the API gateway and the OTel
   collector. Even a fully compromised agent process cannot open a TCP
   connection to an exchange, a database, or the Execution Service — the
   Execution Service's own egress rule is the only one in the namespace
   permitted to reach the public internet.

## Service map

| Service | Owns | Status |
|---|---|---|
| API Gateway | Routing, auth, rate limiting, CORS | Full (YARP) |
| Identity Service | Users, roles, JWT issuance | Skeleton |
| **Market Data Service** | Binance WebSocket, TimescaleDB, Redis cache | **Full** |
| **Trading Service** | Intent lifecycle, the intent→risk workflow | **Full** |
| **Risk Service** | Risk policy, deterministic gate, position sizing | **Full** |
| Execution Service | Exchange orders — the only service with credentials | Skeleton |
| Portfolio Service | Balances, positions, P&L | Skeleton |
| **Strategy Service** | Indicator engine, deterministic signals | **Full** |
| Backtesting Service | Historical simulation | Skeleton |
| Reconciliation Service | Exchange-vs-internal-state comparison | Skeleton |
| Notification Service | Alert delivery | Skeleton |
| Audit Service | Append-only decision record | Skeleton |
| Configuration Service | Non-risk runtime configuration | Skeleton |
| AI Agent Platform | Market/technical/sentiment/portfolio/strategy/research agents | **Full** |
| Dashboard | Operational UI | Full shell; most pages are honest placeholders |

"Skeleton" means: compiles, runs, has real health checks, real OpenTelemetry,
a real database connection and migration (where it owns a database), and a
documented phase when its domain logic lands. It never means a stubbed
response dressed up as real data.

## Why Risk and Trading shipped early

The master specification's phase order puts Risk in Phase 4 and Trading in
Phase 5. They were built in Phase 1 instead because every other phase's value
depends on them existing: a Strategy Service with no risk gate to submit to,
or a Market Data Service with no Trading Service to consume its prices, cannot
be meaningfully exercised end-to-end. Building the spine first means every
later phase plugs into a real workflow instead of a planned one.

## Data flow for one trade

1. A strategy, an AI proposal, or an operator calls
   `POST /api/v1/trading/intents` with an `Idempotency-Key`.
2. `CreateTradingIntentCommandHandler` records a `TradingIntent` in
   `trading_db` (status `Created` → `AwaitingRisk`) and commits — so a crash
   here leaves a recoverable, visible record, not a lost attempt.
3. The handler calls `POST /api/v1/risk/evaluations` on the Risk Service,
   itself idempotency-guarded.
4. `DeterministicRiskEvaluator.Evaluate` runs every check (kill switch,
   trading mode, exchange availability, market data freshness, confidence,
   stop/take-profit presence and direction, volatility, open positions, daily
   loss) and, if all size-independent checks pass, calls `PositionSizer` to
   derive a quantity from the risk budget — fees, slippage and exchange
   precision included — then runs the size-dependent checks (notional cap,
   balance, exposure, concentration) against that real quantity.
5. The decision and every individual check are persisted in `risk_db`
   (`risk_checks` table) — approved or rejected, so "why was this trade not
   taken?" is always answerable from the record alone.
6. The Trading Service applies the decision to the `TradingIntent`
   (`RiskApproved` with the approved quantity, or `RiskRejected` with every
   failing code) and commits.
7. *(Phase 5)* An approved intent is handed to the Execution Service, which is
   the only component that signs a request and sends it to Binance.

## Further reading

- [`phases.md`](phases.md) — the full eleven-phase plan
- [`known-limitations.md`](known-limitations.md) — what Phase 1 deliberately does not do yet
- [`../database/schema.md`](../database/schema.md) — database-per-service layout
- [`../security/`](../security/) — identity, database access scoping
