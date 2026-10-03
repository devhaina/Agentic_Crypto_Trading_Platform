# Service Specifications

## API Gateway
Routes external requests, enforces authentication, rate limits, correlation IDs and observability.

## Identity Service
Manages users, roles, sessions/tokens and authentication integration.

## Market Data Service
Consumes exchange WebSocket streams, normalizes data, stores time-series records and publishes market events.

## Strategy Service
Maintains strategy definitions and versions; computes indicators and emits signals.

## Trading Service
Creates and validates trade intents, coordinates risk approval and tracks order lifecycle.

## Risk Service
Deterministic hard gate. Must remain operationally independent of AI.

## Execution Service
Only service permitted to invoke exchange trading APIs. Implements exchange adapters, signing, idempotency and execution error handling.

## Portfolio Service
Maintains balances, positions, exposure and P&L.

## Reconciliation Service
Periodically compares exchange state and internal state; disables new trading on critical mismatch.

## Agent Platform
Python service containing specialized agents and orchestration. No direct trading credentials.

## Backtesting Service
Runs historical simulation and produces performance reports.

## Notification Service
Delivers operational/risk notifications.

## Audit Service
Stores immutable/auditable records of financial decisions and security events.
