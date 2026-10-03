# API Contracts

Base path: `/api/v1`

## Authentication
POST /auth/login
POST /auth/refresh
POST /auth/logout

## Market
GET /market/ticker/{symbol}
GET /market/candles/{symbol}?timeframe=15m
GET /market/orderbook/{symbol}

## Signals
GET /signals
GET /signals/{id}

## Trading
POST /trading/intents
GET /trading/orders
GET /trading/orders/{id}
POST /trading/orders/{id}/cancel

## Portfolio
GET /portfolio
GET /portfolio/positions
GET /portfolio/pnl

## Risk
GET /risk/status
GET /risk/policies
PUT /risk/policies
POST /risk/kill-switch

## Agents
GET /agents
GET /agents/runs
GET /agents/runs/{id}

## Strategies
GET /strategies
POST /strategies
GET /strategies/{id}
POST /strategies/{id}/backtest
POST /strategies/{id}/activate
POST /strategies/{id}/pause

## Backtesting
POST /backtests
GET /backtests/{id}
GET /backtests/{id}/results

## Audit
GET /audit/events

## Example Trading Intent
```json
{
  "symbol": "BTCUSDT",
  "side": "BUY",
  "quantity": "0.01000000",
  "signalId": "uuid",
  "strategyId": "uuid",
  "clientRequestId": "uuid"
}
```

## Example Risk Response
```json
{
  "decision": "APPROVED",
  "riskCheckId": "uuid",
  "reasonCodes": ["WITHIN_DAILY_LOSS", "WITHIN_EXPOSURE"],
  "maxAllowedQuantity": "0.01000000"
}
```

All public APIs must be OpenAPI documented, versioned and validated.
