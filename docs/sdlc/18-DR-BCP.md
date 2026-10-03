# Disaster Recovery and Business Continuity

## Objectives
Define service recovery priorities and safe trading behavior.

## Priority
P0: Execution/Risk integrity
P1: Market data and reconciliation
P2: Portfolio/trading APIs
P3: Dashboard/analytics

## Safe Failure
If Risk or Reconciliation is unavailable, stop new trading.

## Data
- PostgreSQL backups
- TimescaleDB backups
- Audit retention
- Object storage for long-term reports

## Recovery
1. Disable new trading.
2. Recover infrastructure.
3. Verify database consistency.
4. Restore market data pipeline.
5. Reconcile exchange balances/orders/positions.
6. Validate risk service.
7. Run paper/smoke checks.
8. Re-enable trading only after approval.

Define environment-specific RPO/RTO before production launch.
