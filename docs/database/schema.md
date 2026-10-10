# Database Schema — Database Per Service

## Relational (PostgreSQL)

One database per owning service, provisioned by
`infrastructure/postgres/init/01-create-databases.sql`:

| Database | Owning service | Tables |
|---|---|---|
| `identity_db` | Identity | *(none yet — Phase 11)* |
| `trading_db` | Trading | `trading_intents` + outbox/inbox/idempotency |
| `risk_db` | Risk | `risk_policies`, `risk_checks` + outbox/inbox/idempotency |
| `execution_db` | Execution | `orders` + outbox/inbox/idempotency |
| `portfolio_db` | Portfolio | `positions`, `accounts`, `trades` + outbox/inbox/idempotency |
| `strategy_db` | Strategy | `strategies`, `signals` + outbox/inbox/idempotency |
| `agent_db` | *(reserved)* | Not currently used — the AI platform is stateless in Phase 1 |
| `audit_db` | Audit | *(none yet — Phase 5)* |
| `backtesting_db` | Backtesting | `backtest_runs`, `backtest_trades` + outbox/inbox/idempotency |
| `configuration_db` | Configuration | *(none yet — Phase 11)* |
| `reconciliation_db` | Reconciliation | *(none yet — Phase 6)* |
| `notification_db` | Notification | *(none yet — Phase 11)* |

`execution_db`, `reconciliation_db` and `notification_db` are not in the
engineering specification's illustrative nine-database list; they exist
because "every service owns its own database" (Rule, §4) takes precedence
over that list — those three services would otherwise have nowhere to put
their own state.

Every database gets: UTC timezone, a 10s lock timeout, a 60s
idle-in-transaction timeout (see the init script for why).

## Every service's schema includes three infrastructure tables

Defined once in `Agentiva.BuildingBlocks.Persistence.Abstractions.AgentivaDbContext`
and inherited by every service's `DbContext`:

- **`outbox_messages`** — events awaiting publication, written in the same
  transaction as the state change that raised them. Claimed with
  `FOR UPDATE SKIP LOCKED` so multiple replicas don't double-publish.
- **`inbox_messages`** — consumed-event ledger, keyed on
  `(event_id, consumer_name)`, making at-least-once delivery into
  effectively-once processing.
- **`idempotency_entries`** — financial-command ledger, uniquely indexed on
  the caller's idempotency key. This is the actual concurrency control for
  duplicate commands: a unique-index insert, not a read-then-write check.

## Time-series (TimescaleDB)

One database, `market_db`, schema `market`, provisioned by
`infrastructure/timescale/init/01-market-schema.sql`:

`market_ticks`, `market_trades`, `market_candles`, `orderbook_snapshots`,
`indicator_snapshots`, `portfolio_snapshots` — all hypertables, chunked by
time. Plus `instrument_precision`, a plain reference table caching exchange
filters so the risk and execution services don't round-trip to the exchange
per order. The first four are written by the Market Data Service (Phase 2);
`indicator_snapshots` by the Strategy Service (Phase 3); `portfolio_snapshots`
by the Portfolio Service (Phase 6, one row per processed fill) — all into
this same shared database. See known-limitations.md on why that is
table-level ownership, not a layering slip. As of Phase 8, the Backtesting
Service is one more reader of `market_candles` (via `HistoricalCandleReader`,
raw Npgsql, filtered to `is_closed = TRUE`) — the first consumer of this
table that isn't the service writing it.

## The money type

Every `decimal` property in every service is mapped to `NUMERIC(38,18)` by a
single global EF Core convention
(`AgentivaDbContext.ConfigureConventions`) — not per-property, so a newly
added money column cannot silently default to a lossy precision. Verified by
inspecting the generated migrations: `grep -o 'numeric([0-9]*,[0-9]*)'` on
both the Risk and Trading migrations returns only `numeric(38,18)`, zero
`double precision` or `real` columns.

## Redis

Cache only, never the source of truth (Rule, §4). Used for: the latest tick,
candle and order book per symbol, each with a TTL (Phase 2, written by
`RedisMarketDataCache` in the Market Data Service), the kill-switch and
trading-enabled flags
(read by `PlatformStateProvider` in the Risk Service, which fails *closed* —
treats an unreadable flag as kill-switch-engaged, never as clear).
