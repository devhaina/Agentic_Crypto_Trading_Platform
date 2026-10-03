-- =============================================================================
-- Agentiva — market time-series schema (TimescaleDB)
--
-- Market data has a different shape from the platform's relational data: it is
-- append-only, arrives at thousands of rows per second, is almost always
-- queried by a time range, and is worth progressively less as it ages. That is
-- exactly what a hypertable is for, which is why this lives in its own database
-- and is provisioned with SQL rather than EF Core migrations — EF has no way to
-- express a hypertable, a compression policy or a continuous aggregate.
--
-- Populated from Phase 2, when the Market Data Service connects to Binance.
-- =============================================================================

\set ON_ERROR_STOP on

CREATE EXTENSION IF NOT EXISTS timescaledb;

CREATE SCHEMA IF NOT EXISTS market AUTHORIZATION agentiva;
SET search_path TO market, public;

-- -----------------------------------------------------------------------------
-- Reference data
-- -----------------------------------------------------------------------------

-- Exchange precision filters. Cached here so the risk and execution services
-- can normalise an order without a round trip to the exchange on every trade.
CREATE TABLE IF NOT EXISTS instrument_precision (
    symbol              VARCHAR(24)     PRIMARY KEY,
    exchange            VARCHAR(32)     NOT NULL,
    base_asset          VARCHAR(12)     NOT NULL,
    quote_asset         VARCHAR(12)     NOT NULL,

    -- NUMERIC(38,18) throughout. A float tick size would round, and an order
    -- whose price is off by one rounding step is rejected by the exchange.
    tick_size           NUMERIC(38,18)  NOT NULL CHECK (tick_size > 0),
    step_size           NUMERIC(38,18)  NOT NULL CHECK (step_size > 0),
    min_quantity        NUMERIC(38,18)  NOT NULL CHECK (min_quantity >= 0),
    max_quantity        NUMERIC(38,18)  NOT NULL CHECK (max_quantity > 0),
    min_notional        NUMERIC(38,18)  NOT NULL CHECK (min_notional >= 0),

    is_trading_enabled  BOOLEAN         NOT NULL DEFAULT TRUE,
    refreshed_at        TIMESTAMPTZ     NOT NULL DEFAULT now()
);

COMMENT ON TABLE instrument_precision IS
    'Exchange symbol filters, refreshed periodically. A stale step size causes '
    'every order on the symbol to be rejected, so refreshed_at is monitored.';

-- -----------------------------------------------------------------------------
-- Ticks
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS market_ticks (
    time            TIMESTAMPTZ     NOT NULL,
    symbol          VARCHAR(24)     NOT NULL,
    exchange        VARCHAR(32)     NOT NULL,
    bid_price       NUMERIC(38,18)  NOT NULL,
    bid_quantity    NUMERIC(38,18)  NOT NULL,
    ask_price       NUMERIC(38,18)  NOT NULL,
    ask_quantity    NUMERIC(38,18)  NOT NULL,
    last_price      NUMERIC(38,18)  NOT NULL,

    -- Receipt time as well as exchange time, so clock skew and feed latency are
    -- measurable rather than guessed at. The gap between them is the
    -- market_data_latency metric.
    received_at     TIMESTAMPTZ     NOT NULL DEFAULT now()
);

SELECT create_hypertable(
    'market_ticks', 'time',
    chunk_time_interval => INTERVAL '1 hour',
    if_not_exists => TRUE
);

-- Symbol first, time descending: the overwhelmingly common query is "the
-- latest N ticks for one symbol".
CREATE INDEX IF NOT EXISTS ix_market_ticks_symbol_time
    ON market_ticks (symbol, time DESC);

-- -----------------------------------------------------------------------------
-- Public trades
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS market_trades (
    time                TIMESTAMPTZ     NOT NULL,
    symbol              VARCHAR(24)     NOT NULL,
    exchange            VARCHAR(32)     NOT NULL,
    exchange_trade_id   VARCHAR(64)     NOT NULL,
    price               NUMERIC(38,18)  NOT NULL,
    quantity            NUMERIC(38,18)  NOT NULL,
    buyer_is_maker      BOOLEAN         NOT NULL,
    received_at         TIMESTAMPTZ     NOT NULL DEFAULT now()
);

SELECT create_hypertable(
    'market_trades', 'time',
    chunk_time_interval => INTERVAL '1 hour',
    if_not_exists => TRUE
);

CREATE INDEX IF NOT EXISTS ix_market_trades_symbol_time
    ON market_trades (symbol, time DESC);

-- De-duplicates a replayed WebSocket message after a reconnect, which is a
-- routine event rather than an exceptional one.
CREATE UNIQUE INDEX IF NOT EXISTS ux_market_trades_exchange_id
    ON market_trades (exchange, symbol, exchange_trade_id, time);

-- -----------------------------------------------------------------------------
-- Candles
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS market_candles (
    time            TIMESTAMPTZ     NOT NULL,   -- candle open time
    symbol          VARCHAR(24)     NOT NULL,
    timeframe       VARCHAR(8)      NOT NULL,
    exchange        VARCHAR(32)     NOT NULL,
    open            NUMERIC(38,18)  NOT NULL,
    high            NUMERIC(38,18)  NOT NULL,
    low             NUMERIC(38,18)  NOT NULL,
    close           NUMERIC(38,18)  NOT NULL,
    volume          NUMERIC(38,18)  NOT NULL,
    quote_volume    NUMERIC(38,18)  NOT NULL,
    trade_count     INTEGER         NOT NULL,
    close_time      TIMESTAMPTZ     NOT NULL,

    -- Only closed candles are stored. An in-progress candle can still change,
    -- and a strategy that acted on one would be using look-ahead information
    -- in live trading and producing an irreproducible backtest.
    is_closed       BOOLEAN         NOT NULL DEFAULT TRUE,

    CONSTRAINT ck_market_candles_ohlc CHECK (
        high >= low AND high >= open AND high >= close
        AND low <= open AND low <= close
    )
);

SELECT create_hypertable(
    'market_candles', 'time',
    chunk_time_interval => INTERVAL '7 days',
    if_not_exists => TRUE
);

-- One candle per symbol, timeframe and open time. Makes ingestion idempotent:
-- a reconnect that replays a candle updates rather than duplicates it.
CREATE UNIQUE INDEX IF NOT EXISTS ux_market_candles_key
    ON market_candles (symbol, timeframe, time);

-- -----------------------------------------------------------------------------
-- Order book snapshots
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS orderbook_snapshots (
    time        TIMESTAMPTZ     NOT NULL,
    symbol      VARCHAR(24)     NOT NULL,
    exchange    VARCHAR(32)     NOT NULL,
    update_id   BIGINT          NOT NULL,

    -- Levels as jsonb rather than one row per level. A 20-level book written
    -- per update would be 40 rows per message at thousands of messages per
    -- second; the snapshot is always read whole, so there is nothing to gain
    -- from normalising it.
    bids        JSONB           NOT NULL,
    asks        JSONB           NOT NULL,
    received_at TIMESTAMPTZ     NOT NULL DEFAULT now()
);

SELECT create_hypertable(
    'orderbook_snapshots', 'time',
    chunk_time_interval => INTERVAL '1 hour',
    if_not_exists => TRUE
);

CREATE INDEX IF NOT EXISTS ix_orderbook_symbol_time
    ON orderbook_snapshots (symbol, time DESC);

-- -----------------------------------------------------------------------------
-- Indicator snapshots
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS indicator_snapshots (
    time        TIMESTAMPTZ     NOT NULL,
    symbol      VARCHAR(24)     NOT NULL,
    timeframe   VARCHAR(8)      NOT NULL,

    -- Indicator values keyed by name, e.g. {"RSI_14": "61.2"}. Values are
    -- stored as JSON strings to preserve decimal precision: jsonb numbers are
    -- doubles, and a rounded indicator feeds a rounded stop distance.
    indicators  JSONB           NOT NULL,

    -- The strategy version that computed them. Without it, a historical
    -- indicator value cannot be attributed to the logic that produced it, and
    -- a backtest cannot be reproduced after a strategy change.
    strategy_version VARCHAR(20),
    computed_at TIMESTAMPTZ     NOT NULL DEFAULT now()
);

SELECT create_hypertable(
    'indicator_snapshots', 'time',
    chunk_time_interval => INTERVAL '7 days',
    if_not_exists => TRUE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_indicator_snapshots_key
    ON indicator_snapshots (symbol, timeframe, time);

-- -----------------------------------------------------------------------------
-- Portfolio snapshots
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS portfolio_snapshots (
    time                TIMESTAMPTZ     NOT NULL,
    trading_account_id  UUID            NOT NULL,
    total_value         NUMERIC(38,18)  NOT NULL,
    available_balance   NUMERIC(38,18)  NOT NULL,
    total_exposure      NUMERIC(38,18)  NOT NULL,
    realized_pnl        NUMERIC(38,18)  NOT NULL,
    unrealized_pnl      NUMERIC(38,18)  NOT NULL,
    open_position_count INTEGER         NOT NULL,
    drawdown_percent    NUMERIC(38,18)  NOT NULL,
    quote_asset         VARCHAR(12)     NOT NULL
);

SELECT create_hypertable(
    'portfolio_snapshots', 'time',
    chunk_time_interval => INTERVAL '7 days',
    if_not_exists => TRUE
);

CREATE INDEX IF NOT EXISTS ix_portfolio_snapshots_account_time
    ON portfolio_snapshots (trading_account_id, time DESC);
