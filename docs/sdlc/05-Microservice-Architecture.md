# Microservice Architecture

## Services

| Service | Runtime | Owns |
|---|---|---|
| API Gateway | .NET 10 | Routing/auth edge |
| Identity | .NET 10 | Users/roles |
| Market Data | .NET 10 | Market streams |
| Strategy | .NET 10 | Strategies/signals |
| Trading | .NET 10 | Trading intents/order workflow |
| Risk | .NET 10 | Risk policies/checks |
| Execution | .NET 10 | Exchange commands |
| Portfolio | .NET 10 | Positions/P&L |
| Reconciliation | .NET 10 | Exchange/internal consistency |
| Backtesting | Python/.NET | Simulation |
| Agent Platform | Python | AI agents |
| Notification | .NET 10 | Alerts |
| Audit | .NET 10 | Audit evidence |
| Configuration | .NET 10 | Non-secret runtime configuration |

## Communication
Synchronous: REST/gRPC for immediate decisions.
Asynchronous: RabbitMQ domain events.

## Ownership
Each service owns its database. Cross-service reads use APIs/events, never direct SQL.

## Reliability Patterns
- Outbox
- Idempotent consumers
- Circuit breaker
- Retry with backoff
- Dead-letter queues
- Correlation IDs
- Timeouts
- Bulkheads
- Health/readiness probes
