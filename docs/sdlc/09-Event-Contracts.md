# Event Contracts

## Envelope
```json
{
  "eventId": "uuid",
  "eventType": "order.filled",
  "eventVersion": 1,
  "occurredAt": "2026-10-03T08:20:00Z",
  "correlationId": "uuid",
  "causationId": "uuid",
  "aggregateId": "uuid",
  "producer": "execution-service",
  "payload": {}
}
```

## Events
market.candle.created
market.tick.created
signal.created
agent.analysis.completed
agent.signal.proposed
trade.intent.created
risk.approved
risk.rejected
order.created
order.submitted
order.partially.filled
order.filled
order.cancelled
position.updated
portfolio.updated
reconciliation.failed
risk.limit.breached
trading.disabled

## Rules
- Events are immutable.
- Events are versioned.
- Consumers must be idempotent.
- Unknown event versions must fail safely.
- Sensitive credentials must never appear in events.
- Financial events must contain stable IDs for traceability.
