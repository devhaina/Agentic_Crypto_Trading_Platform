# Solution Architecture

## Logical Architecture

```text
Angular 22
    |
API Gateway (.NET 10/YARP)
    |
+---+-----------------------------+
|                                 |
Identity                     Business APIs
                                 |
       +-----------+-------------+-------------+
       |           |             |             |
    Trading      Risk        Portfolio      Strategy
       |           |             |             |
       +-----------+-------------+-------------+
                       |
                 Execution Service
                       |
                    Binance

RabbitMQ connects asynchronous workflows.
Redis provides low-latency cache/state.
PostgreSQL is service-owned transactional storage.
TimescaleDB stores time-series market data.
Python Agent Platform consumes events and produces proposals.
```

## Trust Boundaries
1. Public/UI boundary
2. API gateway boundary
3. Internal service boundary
4. AI trust boundary
5. Execution trust boundary
6. Exchange boundary

## Core Flow
Market event -> Strategy -> AI analysis -> Trading Intent -> Risk -> Execution -> Exchange -> Fill -> Reconciliation -> Portfolio -> UI.

## Architectural Principles
- Database per service
- Event-driven integration
- Deterministic risk gate
- Exchange adapter isolation
- Zero direct exchange access from AI
- Defense in depth
- Audit everything financially material
