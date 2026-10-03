# Runbook: Indeterminate Order

**Alert:** `OrderOutcomeIndeterminate` (critical, fires immediately)
**Event:** `order.indeterminate` on the event bus

## What this means

A submission to the exchange timed out, or the connection dropped mid-
request, and the platform genuinely does not know whether the order reached
the exchange's book. This is the single most dangerous order state in the
system: **the order may or may not exist.**

## Why no automatic retry happens

Retrying blind is exactly how a platform ends up with twice the intended
position. If the original request *did* reach the exchange, a retry places a
second order; if the client-order-id would make the exchange reject the
retry as a duplicate, you still don't know that without checking. Either way,
guessing is unacceptable at this boundary — see the `OrderIndeterminate`
event contract's remarks for the structural reasoning.

## Immediate automatic response

Automated trading on the **affected symbol** stops. The order is recorded
with `LastKnownAction` describing exactly what was attempted. A critical
alert fires.

## Operator steps

1. Note the order's `ClientOrderId` — this is the deterministic key
   (`AGENTIVA-{SYMBOL}-{DATE}-{SEQ}`) that lets you ask the exchange
   directly, unambiguously, whether it has an order with that client id.
2. Query the exchange's order-status endpoint (or dashboard) by client order
   id, **not** by searching recent orders — a search can miss it or match
   the wrong one.
3. Three possible findings:
   - **Not found on the exchange.** The original request never arrived.
     Safe to resubmit as a **new** intent with a new idempotency key, after
     confirming no partial state was created locally.
   - **Found, unfilled.** The order exists and is live. Record its real
     exchange order id against the local `Order` record manually, then let
     normal order-lifecycle processing resume for it. Do not submit another
     order for the same intent.
   - **Found, filled (partially or fully).** The trade happened. Record the
     fill manually against the local records so the portfolio isn't missing
     it, then let reconciliation confirm the correction on its next run.
4. Re-enable trading on the symbol once you're confident the local and
   exchange state agree for that order specifically — you do not need to wait
   for a full platform-wide reconciliation pass if the investigation above
   was conclusive, but logging what you found in the audit trail either way
   is mandatory.

## Never do this

- Never resubmit the same intent without confirming the exchange's answer
  first.
- Never assume "timeout" means "didn't happen" — it means "unknown."
