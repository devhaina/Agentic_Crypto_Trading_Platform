# Alerting

Rules live in `infrastructure/monitoring/prometheus/rules/agentiva-alerts.yml`
and are grouped by what an operator does about them, not by which service
emits the metric.

## Severity convention

- **Critical** — something that must stop new trading until a human looks at
  it: a reconciliation mismatch, an indeterminate order outcome, the kill
  switch engaging. These page immediately (`for: 0m` or `1m`).
- **Warning** — a degraded capability that is not itself unsafe: the exchange
  connection down (the risk gate already refuses trades without it), market
  data repeatedly stale, the outbox backlog growing, an unusual rejection
  rate, a high agent failure rate. These wait a few minutes before firing, to
  avoid paging on a transient blip.

## Why thresholds are conservative

Every threshold in the committed rule file assumes the Phase 1 paper-trading
configuration — a single small account, no real capital. **Review every
threshold before enabling live trading**; in particular
`DailyDrawdownApproachingLimit` and `RiskRejectionSpike` are tuned for
low-volume paper trading and will fire constantly against a live, actively
traded account unless recalibrated.

## The two alerts that must never be silenced

`ReconciliationFailed` and `OrderOutcomeIndeterminate` represent the platform
no longer being certain its own books match the exchange's. Silencing either
in an alert-fatigue cleanup is equivalent to removing the one check that
would catch a duplicated position before it compounds. If one of these fires
too often, the fix is to find and fix the root cause, never to raise its
threshold or mute it.
