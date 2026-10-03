# Testing Strategy

## Unit
Domain rules, indicators, position sizing, risk decisions.

## Integration
Database, Redis, RabbitMQ, exchange adapter sandbox/test environment.

## Contract
REST OpenAPI contracts and RabbitMQ event schemas.

## E2E
Market event -> strategy -> agent -> risk -> execution -> portfolio.

## Resilience
- service restart
- message duplication
- message delay
- exchange timeout
- WebSocket reconnect
- database failure
- Redis failure
- RabbitMQ failure
- risk service unavailable
- stale market data

## Security
SAST, dependency scanning, secret scanning, container scanning, DAST.

## Performance
Load test API, event throughput, market-data ingestion and portfolio calculations.

## Trading Safety
Tests must prove that:
- risk rejection prevents execution
- duplicate commands don't create duplicate orders
- stale data prevents new trades
- kill switch prevents new trades
- reconciliation mismatch disables new trades.
