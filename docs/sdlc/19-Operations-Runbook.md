# Operations Runbook

## Exchange Outage
1. Detect outage.
2. Stop new trading.
3. Keep reconciliation active if possible.
4. Alert operator.
5. Do not blindly retry order creation.
6. Reconcile when exchange recovers.

## Market Data Stale
1. Stop new trading.
2. Verify WebSocket connection.
3. Restart consumer if required.
4. Verify timestamps.
5. Resume only after freshness threshold passes.

## Duplicate Order Suspected
1. Disable new trading.
2. Query exchange by clientOrderId/order ID.
3. Reconcile internal order state.
4. Record audit event.
5. Resume only after consistency is proven.

## Risk Service Down
No new trades. Existing positions remain under monitoring.

## Reconciliation Failure
Disable new trading, alert operations, investigate exchange/internal state mismatch.

## Kill Switch
Trigger manually or automatically. Record actor/reason/time. Require explicit authorization to re-enable.

## Deployment
Deploy to staging first. Run smoke tests. Verify health and event flow. Never enable live execution automatically.
