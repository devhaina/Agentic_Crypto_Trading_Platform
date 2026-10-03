# Known Limitations — Phase 1

Every limitation below is deliberate and documented at its source in code.
This file collects them in one place so a reviewer does not have to find each
comment individually. None of them weaken the AI trust boundary or the
no-floating-point rule; they are scope limits, not safety shortcuts.

## Trading Service uses a paper-trading baseline, not the real portfolio

`PaperPortfolioSnapshotProvider`
(`services/trading-service/Agentiva.Trading.Infrastructure/Providers/Phase1Providers.cs`)
returns a configured starting equity with zero exposure and zero daily loss,
because the Portfolio Service holds no real state until Phase 6. Practical
effect: the exposure, concentration and daily-loss checks in the Risk Service
cannot meaningfully bind yet, because every intent is evaluated against an
empty, freshly-funded account. The risk-per-trade, stop/take-profit, and
sizing-precision checks are fully real regardless, since they do not depend
on portfolio state.

## The duplicate-order check is a placeholder

`CreateTradingIntentCommandHandler` hard-codes `HasDuplicateOpenOrder: false`
when calling the Risk Service, because there is no order store to check
against yet — the Execution Service, which owns orders, does not exist until
Phase 5. The deterministic client-order-id scheme that is the real defence
against a duplicate exchange order also lands in Phase 5.

## No exchange connection exists

`BINANCE__APIKEY` and `BINANCE__APISECRET` are blank in every committed
configuration file. No service makes an outbound call to Binance in Phase 1.
Market data tools in the AI platform (`get_market_data`, `get_candles`,
`get_indicators`, `get_orderbook`) call the gateway's market routes, which
have no Market Data Service behind them yet and so return an empty payload —
by design: `PlatformReadClient.get()` returns `{}` on any upstream failure
rather than raising, so an agent reasons from absent evidence and reports zero
confidence instead of the whole pipeline failing.

## The AI platform runs on a deterministic stub by default

`AI_LLM_PROVIDER=stub` is the default in every committed environment file.
The stub is not a placeholder to be embarrassed about — it is what makes the
orchestrator's behaviour reproducible enough to assert against in
`tests/test_orchestrator.py`, and it is also the safe fallback when a real
provider is configured without an API key (`create_provider` in
`agentiva_agents/llm/anthropic_provider.py` degrades to the stub and logs
loudly rather than failing to start).

## TimescaleDB schema exists; nothing writes to it yet

`infrastructure/timescale/init/01-market-schema.sql` creates every hypertable
(`market_ticks`, `market_trades`, `market_candles`, `orderbook_snapshots`,
`indicator_snapshots`, `portfolio_snapshots`) and the `instrument_precision`
reference table. The Market Data Service that populates them is Phase 2.

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
