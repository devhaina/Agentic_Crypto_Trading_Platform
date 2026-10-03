# Product Requirements Document

## 1. Product Vision
Build a secure, auditable, AI-assisted crypto trading platform that continuously consumes market data, evaluates deterministic strategies, uses specialized AI agents for analysis, applies hard risk controls, and executes approved trades through an exchange adapter.

## 2. Goals
- Provide real-time market intelligence.
- Generate explainable trading signals.
- Automate paper trading end-to-end.
- Enforce deterministic risk limits.
- Maintain accurate positions and P&L.
- Provide complete auditability.
- Support backtesting and strategy versioning.
- Enable controlled live trading only after validation.

## 3. Non-Goals
- Guaranteed profit or daily returns.
- Autonomous withdrawals.
- AI-controlled exchange credentials.
- Unreviewed self-modifying production strategies.
- High-leverage trading in MVP.

## 4. Personas
- Administrator
- Trader/Operator
- Quant/Strategy Engineer
- AI Engineer
- Auditor
- Read-only Analyst

## 5. MVP
Exchange: Binance
Assets: BTCUSDT, ETHUSDT
Modes: Backtest, Paper; Live behind explicit feature gate
Timeframes: 15m, 1h
Strategies: EMA/RSI, trend following, breakout
Agents: Market, Technical, Strategy, Portfolio, Risk analysis
Dashboard: portfolio, positions, orders, signals, agents, risk, backtests, audit.

## 6. Success Criteria
- Market data remains available through exchange disconnect/reconnect cycles.
- Duplicate commands do not create duplicate exchange orders.
- Risk limits are enforced independently of AI.
- Every live/paper order can be traced from signal to execution and P&L.
- Backtests account for fees and slippage.
- Reconciliation detects exchange/internal mismatches.
- All critical services expose health and telemetry.

## 7. Product Risks
- Market volatility
- Exchange outages
- API changes
- Model hallucination
- Data leakage in backtesting
- Overfitting
- Duplicate order execution
- Stale market data
- Reconciliation failure
- Credential compromise
