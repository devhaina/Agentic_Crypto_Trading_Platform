# Software Requirements Specification

## Functional Requirements

### FR-001 Authentication
System shall authenticate users using OAuth2/OIDC-compatible mechanisms.

### FR-002 RBAC
System shall enforce ADMIN, TRADER, VIEWER, AUDITOR roles.

### FR-003 Market Data
System shall ingest configured Binance market streams and normalize them.

### FR-004 Strategy
System shall calculate indicators and generate versioned signals.

### FR-005 Agent Analysis
System shall run specialized AI agents and store agent runs/outputs.

### FR-006 Risk
System shall independently validate every trading intent.

### FR-007 Execution
Only Execution Service shall access exchange trading endpoints.

### FR-008 Portfolio
System shall maintain balances, positions and P&L.

### FR-009 Reconciliation
System shall compare internal state with exchange state.

### FR-010 Backtesting
System shall simulate historical strategies including fees/slippage.

### FR-011 Audit
System shall record immutable evidence for critical actions.

### FR-012 Notifications
System shall notify operators of critical system/risk events.

## Non-Functional Requirements
- Availability: target 99.9% for control-plane services.
- API latency: p95 under 300ms for non-market-data CRUD operations under normal load.
- Security: secrets externalized; least privilege.
- Scalability: independent horizontal scaling.
- Reliability: retries, circuit breakers, idempotency, outbox.
- Observability: logs, metrics, traces.
- Recovery: documented RPO/RTO.
- Maintainability: independent service ownership and contracts.
