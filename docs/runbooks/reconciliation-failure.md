# Runbook: Reconciliation Failure

**Alert:** `ReconciliationFailed` (critical, fires immediately)
**Event:** `reconciliation.failed` on the event bus

## What this means

The platform's internal record of balances, orders, fills, or positions
disagrees with what the exchange reports. **Every risk calculation downstream
of this mismatch is suspect** — the Risk Service's exposure and concentration
checks are only as correct as the portfolio data they're measured against.

## Immediate automatic response

By design (Rule, §18), the Reconciliation Service does **not** attempt to
silently fix the discrepancy. On detection it:

1. Disables new trading (`Trading__Mode` effectively frozen; new intents are
   rejected by the Risk Service's `TradingDisabled` check).
2. Raises a `CRITICAL` `SystemAlert`.
3. Writes an `AuditEvent` with every field of the mismatch.
4. Notifies operators via the Notification Service.

Existing positions are left untouched. **Nothing is automatically closed or
adjusted.**

## Operator steps

1. Read the alert's `mismatchType` (`BALANCE`, `ORDER`, `FILL`, or
   `POSITION`) and the full `mismatches` list — each entry gives the field,
   the internal value, and the exchange value.
2. Query `audit_db` for the `ReconciliationFailed` event's correlation id to
   see every decision that happened in the window leading up to it.
3. Determine the cause before touching anything:
   - A missed `order.filled` event (check the RabbitMQ dead-letter queue for
     the portfolio service — `agentiva.portfolio-service.dlq`).
   - A duplicate fill applied twice (check the portfolio service's inbox
     table for the event id — it should appear exactly once).
   - A manual action taken directly on the exchange, outside the platform.
4. Fix the root cause first. **Do not** manually edit a balance or position
   row to "match" the exchange without understanding why they diverged — that
   turns a detectable bug into an invisible one.
5. Once corrected, re-run reconciliation manually (or wait for the next
   scheduled run) and confirm `reconciliation.succeeded`.
6. Re-enable trading explicitly — this requires a named operator (see
   `TradingEnabled`, which always carries a non-`SYSTEM` `EnabledBy`).

## Never do this

- Never re-enable trading before the root cause is understood, "to see if it
  happens again."
- Never adjust exchange-side state (e.g. manually placing a corrective order)
  without first disabling the platform's own automated trading — a system
  still trying to trade while you're correcting its books will race you.
