# Going Live

This is deliberately a two-step, two-variable process, so that **one** typo,
one bad environment-file merge, or one copied `.env` cannot enable real-money
trading.

## The two variables

```
TRADING__MODE=LIVE
TRADING__ALLOWLIVE=true
```

`TradingOptions.EffectiveMode` (in `Agentiva.BuildingBlocks.Application`)
returns `PAPER` unless **both** are set. If only `Mode=LIVE` is set without
`AllowLive=true`, every service logs a loud startup warning
(`IsLiveRequestedButBlocked`) and runs in `PAPER` anyway — refusing to start
would be a worse outcome than running safely in the wrong-looking mode, and
the discrepancy is unmissable in the logs and on the dashboard's mode banner.

## Before setting both

- [ ] Exchange API key issued with: trading enabled, **withdrawals disabled**,
      IP-restricted to the cluster's known egress range.
- [ ] Alert thresholds in
      `infrastructure/monitoring/prometheus/rules/agentiva-alerts.yml`
      recalibrated for the real account size (see
      [`alerting.md`](alerting.md)).
- [ ] Reconciliation Service running and verified against the real exchange
      account (Phase 6).
- [ ] Default risk policy reviewed by whoever owns the trading account — the
      shipped defaults (0.5% risk/trade, 2% daily loss, 50% max exposure) are
      intentionally conservative placeholders, not a recommendation for any
      specific account size.
- [ ] A rollback plan: how `TRADING__KILLSWITCHENABLED=true` gets set in an
      emergency, and who is authorised to set it.

## There is no automatic path back to LIVE after a kill-switch event

`KillSwitchDeactivated` requires an operator's user id — the system never
clears its own kill switch (see `RiskEvaluationResult` and the `/risk`
endpoints). Re-enabling live trading after an incident is a deliberate act by
a named person, every time.
