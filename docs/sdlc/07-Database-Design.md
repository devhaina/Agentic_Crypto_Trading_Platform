# Database Design

## Database-per-Service
identity_db, trading_db, risk_db, portfolio_db, strategy_db, agent_db, audit_db, backtesting_db, configuration_db.

## Core Tables

### identity_db
users, roles, user_roles, sessions

### trading_db
trading_intents, orders, order_executions, trades, outbox_messages

### risk_db
risk_policies, risk_checks, risk_events

### portfolio_db
balances, positions, portfolio_snapshots

### strategy_db
strategies, strategy_versions, trading_signals

### agent_db
agent_runs, agent_decisions, agent_tool_calls

### audit_db
audit_events, security_events

### backtesting_db
backtests, backtest_runs, backtest_trades, performance_metrics

### market TimescaleDB
market_ticks, market_candles, orderbook_snapshots, indicator_snapshots

## Financial Types
Use NUMERIC(38,18) or appropriate precision. Never use float/double for financial amounts.

## Key Constraints
- Unique client_order_id.
- Unique exchange order identifier per exchange/account.
- Foreign keys within service boundaries.
- Optimistic concurrency on mutable aggregates.
- Append-only audit events where possible.

## Example Order
```sql
CREATE TABLE orders (
    id UUID PRIMARY KEY,
    account_id UUID NOT NULL,
    client_order_id VARCHAR(100) NOT NULL UNIQUE,
    exchange_order_id VARCHAR(100),
    symbol VARCHAR(30) NOT NULL,
    side VARCHAR(10) NOT NULL,
    order_type VARCHAR(30) NOT NULL,
    quantity NUMERIC(38,18) NOT NULL,
    price NUMERIC(38,18),
    status VARCHAR(30) NOT NULL,
    trading_intent_id UUID,
    risk_check_id UUID,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

## Time-Series
Use Timescale hypertables for candles/ticks/orderbook snapshots.
