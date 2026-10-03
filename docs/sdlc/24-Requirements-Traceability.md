# Requirements Traceability Matrix

| Requirement | Component | Test Evidence |
|---|---|---|
| FR-001 Authentication | Identity/API Gateway | Auth integration tests |
| FR-002 RBAC | Identity/API Gateway | Authorization tests |
| FR-003 Market Data | Market Data Service | WebSocket integration tests |
| FR-004 Strategy | Strategy Service | Strategy unit tests |
| FR-005 Agent Analysis | Agent Platform | Agent contract tests |
| FR-006 Risk | Risk Service | Risk unit/integration tests |
| FR-007 Execution | Execution Service | Exchange adapter tests |
| FR-008 Portfolio | Portfolio Service | Portfolio integration tests |
| FR-009 Reconciliation | Reconciliation Service | Reconciliation tests |
| FR-010 Backtesting | Backtesting Service | Backtest regression tests |
| FR-011 Audit | Audit Service | Audit integration tests |
| FR-012 Notifications | Notification Service | Notification tests |

## Safety Requirements
| Rule | Enforcement |
|---|---|
| AI cannot execute | Network/service authorization |
| Risk approval required | Trading workflow invariant |
| Duplicate order prevention | Idempotency + unique constraints |
| Stale data blocks trades | Risk Service |
| Reconciliation failure blocks trades | Reconciliation + Risk |
| Withdrawals disabled | Exchange key policy |
