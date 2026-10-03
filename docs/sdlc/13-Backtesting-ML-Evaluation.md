# Backtesting and ML Evaluation

## Backtesting Rules
- Use chronological splits.
- Never leak future data into features.
- Include fees.
- Include slippage.
- Model spread where data permits.
- Respect exchange quantity/price filters.
- Model latency assumptions.
- Record strategy version.

## Metrics
Return, CAGR, max drawdown, Sharpe, Sortino, profit factor, win rate, average win/loss, turnover, exposure, fee impact.

## Validation
Use:
1. Training/development period
2. Validation period
3. Out-of-sample test
4. Paper trading

## ML Evaluation
Compare model against:
- naive baseline
- deterministic strategy baseline
- previous production version

Do not promote a model based on one metric.

## Promotion
DRAFT -> BACKTESTING -> VALIDATED -> PAPER -> APPROVED -> ACTIVE -> PAUSED/RETIRED.

## Drift
Monitor performance drift, feature drift and market-regime changes.
