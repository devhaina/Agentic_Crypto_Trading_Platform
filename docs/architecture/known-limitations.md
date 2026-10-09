# Known Limitations

Every limitation below is deliberate and documented at its source in code.
This file collects them in one place so a reviewer does not have to find each
comment individually. None of them weaken the AI trust boundary or the
no-floating-point rule; they are scope limits, not safety shortcuts.

## Trading Service uses an operator-adjustable paper ledger, not the real portfolio

`PaperPortfolioSnapshotProvider`
(`services/trading-service/Agentiva.Trading.Infrastructure/Providers/Phase1Providers.cs`)
reads a Redis-backed ledger an operator can set through
`PUT /api/v1/trading/paper-ledger` (Phase 4), falling back to a configured
starting equity with zero exposure and zero daily loss only when nothing has
been set. This replaced the Phase 1 hardcoded constant, but it is still
simulated state, not a real portfolio: nothing tracks fills or derives
exposure from actual trades, and the risk gate's per-symbol exposure figure
is always zero regardless of what the ledger holds. Practical effect
unchanged from Phase 1: the exposure, concentration and daily-loss checks in
the Risk Service cannot meaningfully bind to real trading history, because
nothing here is derived from real trading history. The risk-per-trade,
stop/take-profit, and sizing-precision checks are fully real regardless,
since they do not depend on portfolio state. Phase 5 added the real fills
(Execution Service); genuine portfolio truth still needs real position
tracking from those fills (Portfolio Service, Phase 6).

## The duplicate-order check is real as of Phase 5, with one gap

`CreateTradingIntentCommandHandler` now calls the Execution Service's
`GET /api/v1/orders/open` before submitting to the Risk Service, so
`HasDuplicateOpenOrder` reflects whatever the order store actually holds
rather than the Phase 1 hard-coded `false`. An unreachable Execution Service
fails closed — treated as "open" — rather than defaulting to "clear". The
deterministic client-order-id scheme (`ClientOrderIdGenerator`, derived from
the command's own idempotency key) is the second, independent line of
defence the risk check's remarks describe.

## A resting order has no path back to the intent that created it

`CreateTradingIntentCommandHandler` advances the intent to `Executed` or
`Failed` synchronously, from the Execution Service's response to the same
HTTP call that placed the order — correct for the common case, a market
order that fills immediately. An order that rests on the book unfilled
(`Submitted`/`PartiallyFilled` — only reachable in `Live` mode, since
Backtest makes no exchange contact and Paper always fills instantly) leaves
its intent parked at `Executing` indefinitely: no consumer in the Trading
Service subscribes to `order.filled`/`order.partiallyFilled` to advance it
later. `TradingIntentConfiguration`'s row-version comment anticipated this
consumer; it does not exist yet, and building it is exactly the kind of work
the Portfolio Service's event consumption (Phase 6) will also need.

## A real exchange connection exists as of Phase 5, gated by trading mode

`BinanceExecutionAdapter` (Execution Service) holds the only Binance API
key/secret in the platform and is the only code path that places a real
order — see `Only_the_execution_service_references_exchange_credentials` in
the architecture test suite. It is reached only when the Execution Service's
own `TradingOptions.EffectiveMode` resolves to `Live`, which additionally
requires `Trading:AllowLive=true`; every committed configuration file ships
`Trading__Mode=PAPER` and blank Binance credentials, so no committed
environment ever reaches a real exchange by default. Backtest and Paper
route to `SimulatedExchangeExecution` instead, which fills an order
instantly at the caller's own reference price with zero fee or slippage —
deliberately not a backtesting-grade fill model; that belongs to the
Backtesting Service (Phase 8), which can model it against historical depth.
The Market Data Service's Binance WebSocket connections (Phase 2) remain a
separate, unauthenticated public market-data path: they can observe the
tape but cannot place an order or read account state.

`get_market_data`, `get_candles`, `get_orderbook` and, as of Phase 3,
`get_indicators` in the AI platform now return real data once the relevant
service has produced at least one value for the requested symbol.
`PlatformReadClient.get()` still returns `{}` on any upstream failure (a 404
before that first value exists, or before the candle buffer below has warmed
up) rather than raising, so an agent reasons from absent evidence and reports
zero confidence instead of the whole pipeline failing.

## The cached ticker's volatility field is still always null — a separate key now carries it

`RedisMarketDataCache` (Market Data Service) still writes
`VolatilityPercent: null` into the `agentiva:market:tick:{symbol}` entry on
every tick — that has not changed, and will not, since the Market Data
Service has no indicator engine of its own. As of Phase 4, the Risk Service's
`MaxVolatilityPercent` check no longer reads that field at all: the Trading
Service's `RedisMarketConditionProvider` now also reads
`agentiva:market:volatility:{symbol}`, written by the Strategy Service from
its ATR via `IndicatorEngine.AnnualizedVolatilityPercent` once it has enough
candle history. That estimate is a proxy, not a model — ATR mixes trend and
noise, and the formula treats every symbol's recent range as equally
representative of its future one. It is a real, moving number in place of a
hardcoded zero, not a statistically rigorous volatility forecast. It is also
unavailable (and the check falls back to the same optimistic zero as before)
until the Strategy Service's candle buffer has warmed up — see below.

## The candle buffer warms up from nothing on every restart

`CandleBufferStore` (Strategy Service) is built entirely from consumed
`market.candle.created` events; this service makes no call back to the
Market Data Service to pre-populate itself from history. Practical effect:
every strategy holds at `INSUFFICIENT_DATA` until enough live candles have
arrived after a (re)start — 27 for the EMA/RSI strategy, 200 for
trend-following — which on the default 15-minute timeframe is up to roughly
two days of real uptime before trend-following can produce its first signal.
This is a deliberate decoupling (no service-to-service HTTP dependency for
history), not an oversight, but it does mean a restart is not transparent to
strategy output the way it is to market data ingestion.

## The kill switch has no durable record beyond its own published event

`PlatformStateProvider.EngageKillSwitchAsync`/`ReleaseKillSwitchAsync` (Risk
Service, Phase 4) write a plain Redis flag and publish
`KillSwitchActivated`/`KillSwitchDeactivated`. If Redis is flushed or
restarted with no consumer having durably recorded those events — the Audit
Service has no real state until Phase 5 — the kill switch's history is gone
and its current state reverts to whatever configuration says (`false`
unless an operator set `TRADING__KILLSWITCHENABLED=true`). The comment on
`PlatformStateProvider` has said since Phase 1 that "the durable record of a
kill switch activation is the audit event"; Phase 4 makes that event real,
but nothing downstream persists it yet. `PendingOrdersCancelled` and
`PositionsLiquidated` are also always `false` on activation: the Execution
Service exists as of Phase 5 and could in principle have open orders to
cancel, but engaging the kill switch does not yet trigger that — no consumer
subscribes to `KillSwitchActivated` to cancel resting orders. Automatic
*liquidation* is never enabled regardless of phase — see the AI trust
boundary notes.

## Strategy performance is signal counts, not a win rate

`GetAllStrategyPerformanceQuery`/`GetStrategyPerformanceQuery` (Strategy
Service) report total/buy/sell signal counts and first/last signal time —
nothing else. A win rate, a Sharpe ratio, or any other outcome-based metric
needs to know what actually happened to a signal after it was produced.
Phase 5 added the filled order; nothing yet traces a signal to the intent
and order it produced, and a realised P&L still needs position tracking
(Phase 6) and/or backtesting (Phase 8). Reporting one now would be either
fabricated or silently wrong, which is exactly the "no page anywhere
displays a fabricated... P&L figure" rule below applied to an API response
instead of a dashboard page.

## The AI platform runs on a deterministic stub by default

`AI_LLM_PROVIDER=stub` is the default in every committed environment file.
The stub is not a placeholder to be embarrassed about — it is what makes the
orchestrator's behaviour reproducible enough to assert against in
`tests/test_orchestrator.py`, and it is also the safe fallback when a real
provider is configured without an API key (`create_provider` in
`agentiva_agents/llm/anthropic_provider.py` degrades to the stub and logs
loudly rather than failing to start).

## TimescaleDB: five of six hypertables are now populated

`infrastructure/timescale/init/01-market-schema.sql` creates every hypertable.
The Market Data Service writes `market_ticks`, `market_trades`,
`market_candles` and `orderbook_snapshots` (Phase 2); the Strategy Service
writes `indicator_snapshots` (Phase 3) into the same shared `market_db` — see
the remarks on `IndicatorSnapshotWriter` for why one table in another
service's connection string is table-level ownership, not a layering slip.
`portfolio_snapshots` (Phase 6) remains provisioned but empty.

## Most dashboard pages are explicit placeholders

Eleven of thirteen dashboard routes render a page stating plainly which phase
fills them and what that phase adds — see
`frontend/src/app/features/*/*.component.html`. The Dashboard and AI Agents
pages are fully live against real backend data. No page anywhere displays a
fabricated balance, P&L figure, or position.

## Identity Service issues no real tokens yet

The Identity Service is a compiling skeleton with health checks and an
`identity_db` connection; it has no user store, no login endpoint, and issues
no JWTs. The JWT *validation* machinery in `ServiceDefaults` (issuer,
audience, symmetric key, policies) is fully wired and ready for Phase 11,
when it is replaced with asymmetric OIDC keys per
[`../security/identity.md`](../security/identity.md).
