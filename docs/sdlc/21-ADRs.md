# Architecture Decision Records

## ADR-001 Microservices
Decision: Use independently deployable microservices.
Reason: Isolation of risk, execution, market data and AI workloads.

## ADR-002 AI Cannot Execute Directly
Decision: AI agents cannot access exchange execution APIs.
Reason: Reduce blast radius and make financial authority deterministic.

## ADR-003 Database per Service
Decision: Each service owns its data.
Reason: Reduce coupling and enable independent scaling.

## ADR-004 RabbitMQ for Domain Events
Decision: Use RabbitMQ for asynchronous workflows.
Reason: Decoupling, retries and event-driven processing.

## ADR-005 TimescaleDB for Market Data
Decision: Use TimescaleDB for high-volume time-series data.
Reason: Efficient time-series storage and querying.

## ADR-006 Redis for Low-Latency State
Decision: Redis is cache/ephemeral state, not source of truth.
Reason: Fast access without compromising durable state.

## ADR-007 Exchange Adapter
Decision: Isolate Binance behind IExchangeExecution.
Reason: Future multi-exchange support and exchange API change isolation.

## ADR-008 Paper Before Live
Decision: Paper trading is mandatory before live rollout.
Reason: Validate end-to-end behavior without real capital.

## ADR-009 Outbox
Decision: Use Outbox Pattern for critical domain events.
Reason: Prevent DB/event inconsistency.
