# Trading and Risk Specification

## Trading Modes
BACKTEST, PAPER, LIVE.

Default development mode: PAPER.

## Risk Parameters
- maxRiskPerTrade
- maxDailyLoss
- maxPortfolioExposure
- maxAssetExposure
- maxOpenPositions
- maxPositionSize
- minSignalConfidence
- requireStopLoss
- requireTakeProfit
- maxMarketDataAge
- maxVolatility

## Position Sizing
Risk amount = equity * risk percentage.
Position size = risk amount / absolute(entry - stop).

Apply balance, precision, fee and slippage constraints afterward.

## Risk Decision
APPROVED or REJECTED with stable reason codes.

## Kill Switch
Stops new trading on critical system/risk conditions. Creates alert and audit event.

## Risk Invariants
1. No risk approval => no execution.
2. Stale market data => no new trade.
3. Risk service unavailable => no new trade.
4. Reconciliation critical failure => no new trade.
5. Duplicate client order ID => do not create another exchange order.
6. Withdrawal permission must remain disabled.

## Paper Trading
Paper execution must use the same trading/risk workflow as live execution wherever possible, with only the exchange adapter swapped.
