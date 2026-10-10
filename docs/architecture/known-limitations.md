# Known Limitations

Every limitation below is deliberate and documented at its source in code.
This file collects them in one place so a reviewer does not have to find each
comment individually. None of them weaken the AI trust boundary or the
no-floating-point rule; they are scope limits, not safety shortcuts.

## The portfolio is real as of Phase 6, with two gaps in how it is announced

`PortfolioServiceClient` (Trading Service) replaced the Phase 1/4 paper
ledger outright: `IPortfolioSnapshotProvider.GetAsync` now calls the
Portfolio Service, which derives equity, available cash, total and
per-symbol exposure, open position count and today's P&L from the `Position`
and `PortfolioAccount` aggregates `order.filled`/`order.partiallyFilled`
actually built — including a real per-symbol exposure figure, which the
paper ledger could never report. An unreachable Portfolio Service fails the
intent closed (`RiskUnavailable`), never a fabricated snapshot.

Two things this real portfolio still does not do. First, `PortfolioUpdated`
and `PnlUpdated` — the account-level contracts in
`contracts/events/.../Portfolio/PortfolioEvents.cs` — are not published as
events; only `PositionUpdated`, `BalanceUpdated` and `TradeCompleted` are,
because the first two need a cross-aggregate read (every open position, at a
live price) that does not naturally live on either aggregate's own
`SaveChanges`. A consumer wanting a live portfolio-level delta today has to
poll the snapshot endpoint. Second, `GetPortfolioSnapshotQueryHandler`'s
`DailyPnl` adds *every* open position's current unrealised P&L to today's
realised trades, not just today's price movement on positions that were
already open at UTC midnight — an honest approximation absent a
start-of-day mark snapshot, not a precise daily figure.

## The duplicate-order check is real as of Phase 5, with one gap

`CreateTradingIntentCommandHandler` now calls the Execution Service's
`GET /api/v1/orders/open` before submitting to the Risk Service, so
`HasDuplicateOpenOrder` reflects whatever the order store actually holds
rather than the Phase 1 hard-coded `false`. An unreachable Execution Service
fails closed — treated as "open" — rather than defaulting to "clear". The
deterministic client-order-id scheme (`ClientOrderIdGenerator`, derived from
the command's own idempotency key) is the second, independent line of
defence the risk check's remarks describe.

## `OrderPartiallyFilledHandler` treats a cumulative figure as an increment

`OrderPartiallyFilled.CumulativeFilledQuantity` is cumulative across the
order's whole life, not one delivery's increment — deliberately, so the
event itself is idempotent on redelivery. The Portfolio Service's
`OrderPartiallyFilledHandler` applies it to `Position`/`PortfolioAccount` as
if it were the increment, which is only correct because no code path in the
platform today raises more than one `OrderPartiallyFilled` per order —
`SubmitOrderCommandHandler` derives it from a single synchronous placement
response, never from a later poll. A future phase that makes a limit order
rest and fill across several deliveries needs this handler to track each
order's previously-applied cumulative quantity and apply only the
difference; it does not yet, because nothing can exercise the gap today.

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
consumer; it still does not exist. Phase 6 built the identical
shape of consumer for the Portfolio Service's own aggregates
(`OrderFilledHandler`, `OrderPartiallyFilledHandler`) — proof the pattern
works, not a substitute for the Trading Service's own missing one.

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
Realised P&L itself is real as of Phase 6 (`Position.RealizedPnl`,
`Trade`), but nothing traces a signal to the intent, order and position it
produced — `OrderFilled` carries no `SignalId`/`StrategyId`, so
`TradeCompletedDomainEvent.SignalId`/`StrategyId` are always null. Reporting
a win rate on this endpoint now would be either fabricated or silently
wrong, which is exactly the "no page anywhere displays a fabricated... P&L
figure" rule below applied to an API response instead of a dashboard page.

## The news tool's exact auth convention is an educated guess

`NewsClient` (`agentiva_agents/tools/news_tools.py`, Phase 7) sends the
configured key as an `api_key` query parameter to CryptoCompare/CoinDesk
Data API — the most-documented convention for this API family, but it could
not be confirmed against a real key: every parameter-name and header
variant tried against the live endpoint returned the identical generic 401,
so there was nothing to distinguish a wrong guess from a wrong key. If this
guess is wrong, the practical effect is `get_news` reporting `unavailable`
instead of real headlines — the same fail-safe outcome as the key being
revoked or the service being down, never a crash or a fabricated result.

## `get_portfolio`/`get_positions` do not match the real Phase 6 endpoints

`register_market_tools` (`agentiva_agents/tools/market_tools.py`) calls
`GET /portfolio/summary` for `get_portfolio`, and neither tool passes a
`tradingAccountId`. The real Portfolio Service built in Phase 6 has no
`/summary` route — only `/positions` and `/snapshot` — and both require
`tradingAccountId` as a query parameter (see the dashboard's own
`DEFAULT_TRADING_ACCOUNT_ID` convention above). `PlatformReadClient.get()`
fails safe on the resulting 404 (returns `{}`), so the portfolio agent has
likely never seen real portfolio data through the gateway — a gap that
predates Phase 7 and is flagged here rather than fixed, since closing it is
a Trading Service / gateway routing change outside Phase 7's scope.

## A backtest runs synchronously within its own HTTP request

`RunBacktestCommandHandler` (Backtesting Service, Phase 8) resolves the
strategy, reads historical candles, simulates, scores, and persists the
result all within the request that triggers it — no background job queue,
no polling-for-status endpoint. A deliberate scope decision for a bounded
CPU computation over data already in the database, not a limitation
discovered after shipping: see the remarks on `RunBacktestCommand` itself.
A backtest period long enough to make the request slow (many years of
sub-minute candles) is the trigger for building a job queue in a later
phase, not a reason to build one speculatively now.

## Walk-forward validation checks consistency, not parameter re-optimisation

`PerformanceCalculator.SplitIntoWindows` (Backtesting Service, Phase 8)
slices a run into sequential windows and scores each independently, which
is the half of walk-forward validation that catches performance
concentrated in one lucky window. The other half — re-fitting a strategy's
tunable parameters on each window's in-sample data before scoring it
out-of-sample — has nothing to re-fit: `EMA_RSI`, `BREAKOUT` and
`TREND_FOLLOWING` each hardcode their periods as constants in code,
deliberately, so a live signal is reproducible. See the remarks on
`WalkForwardWindow` for the full reasoning.

## A backtest trades one position at a time, never reversing on a new signal

`BacktestSimulator` (Backtesting Service, Phase 8) ignores every signal
while a position is open, checking only its own stop and take-profit until
it closes; the earliest a new entry can land is the bar after that. Real
trading-system backtesters often model an opposite signal as an immediate
reverse (close and re-open in one bar) or allow pyramiding into the same
side. Neither is modelled here — a deliberate simplification that keeps
position sizing and equity bookkeeping unambiguous, documented on
`BacktestSimulator` itself, not a bug found after the fact.

## The AI platform runs on a deterministic stub by default

`AI_LLM_PROVIDER=stub` is the default in every committed environment file.
The stub is not a placeholder to be embarrassed about — it is what makes the
orchestrator's behaviour reproducible enough to assert against in
`tests/test_orchestrator.py`, and it is also the safe fallback when a real
provider is configured without an API key (`create_provider` in
`agentiva_agents/llm/anthropic_provider.py` degrades to the stub and logs
loudly rather than failing to start).

## TimescaleDB: every hypertable is now populated

`infrastructure/timescale/init/01-market-schema.sql` creates every hypertable.
The Market Data Service writes `market_ticks`, `market_trades`,
`market_candles` and `orderbook_snapshots` (Phase 2); the Strategy Service
writes `indicator_snapshots` (Phase 3); the Portfolio Service writes
`portfolio_snapshots` (Phase 6, via `PortfolioSnapshotWriter`, one row per
processed fill) — all into the same shared `market_db`. See the remarks on
`IndicatorSnapshotWriter` for why a table in another service's connection
string is table-level ownership, not a layering slip. `drawdown_percent` on
each row is peak-to-trough against every `total_value` this account has
ever recorded in the table, including rows from a prior deployment — there
is no separate high-water-mark table kept in sync.

## Most dashboard pages are explicit placeholders

Nine of thirteen dashboard routes render a page stating plainly which phase
fills them and what that phase adds — see
`frontend/src/app/features/*/*.component.html`. The Dashboard, AI Agents and
Positions pages are fully live against real backend data. No page anywhere
displays a fabricated balance, P&L figure, or position.

Every position and intent on these pages is read against one fixed,
caller-supplied `tradingAccountId` (`DEFAULT_TRADING_ACCOUNT_ID`,
`00000000-0000-0000-0000-000000000001`) rather than a logged-in user's own
account — there is no Identity Service behind either page yet, and no
account-selection UI. A trading intent created for a real end-to-end demo
needs to be sent with that same account id for its resulting position to
show up here. Deliberately not `Guid.Empty` (all zeros): a first attempt at
this convention used that value and was rejected outright by
`CreateTradingIntentCommandValidator`'s `NotEmpty()` check on
`TradingAccountId` — caught only by actually creating an intent end-to-end
through the real HTTP stack, not by any test or review.

## Identity Service issues no real tokens yet

The Identity Service is a compiling skeleton with health checks and an
`identity_db` connection; it has no user store, no login endpoint, and issues
no JWTs. The JWT *validation* machinery in `ServiceDefaults` (issuer,
audience, symmetric key, policies) is fully wired and ready for Phase 11,
when it is replaced with asymmetric OIDC keys per
[`../security/identity.md`](../security/identity.md).
