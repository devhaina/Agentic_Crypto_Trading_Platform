# Observability

## Stack
OpenTelemetry + Prometheus + Grafana + Loki.

## Golden Signals
Latency, traffic, errors, saturation.

## Trading Metrics
- orders submitted
- orders filled
- order failures
- fill latency
- slippage
- fees
- daily P&L
- drawdown
- exposure

## AI Metrics
- agent run count
- agent latency
- model errors
- tool failures
- proposal count
- rejection count
- token usage

## Platform Alerts
- exchange disconnected
- stale market data
- risk service unavailable
- reconciliation failure
- unexpected order
- high error rate
- queue backlog
- database unavailable

## Correlation
Every critical workflow carries correlationId, causationId, signalId, riskCheckId, tradingIntentId, orderId and exchangeOrderId.
