# Angular 22 Architecture

## Feature Areas
- dashboard
- market
- trading
- portfolio
- agents
- strategies
- backtesting
- risk
- audit
- administration

## Core
auth, guards, interceptors, API client, realtime client, error handling, notifications.

## State
Use local component state by default. Use NgRx only for shared application state such as authenticated user, portfolio summary, active positions and live market state.

## Realtime
Use SignalR/WebSocket gateway for:
- prices
- signals
- order updates
- positions
- risk alerts
- agent status

## UX Requirements
- Clear PAPER/LIVE mode indicator.
- Kill-switch state always visible.
- Risk status visible on trading screens.
- Confirmation for manual trading actions.
- No credentials displayed.
- Show order/execution IDs for traceability.
