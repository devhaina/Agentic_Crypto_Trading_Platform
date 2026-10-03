/**
 * Shared API models.
 *
 * Every monetary value is typed `string`, not `number`. This is deliberate and
 * is the most important convention in the client.
 *
 * The backend serialises decimals as JSON strings because JavaScript's
 * `JSON.parse` produces IEEE-754 doubles, and a price such as
 * `100250.123456789012345678` loses its trailing digits irrecoverably the
 * moment it becomes a `number`. Typing these as `string` makes that loss
 * impossible to cause by accident: arithmetic on them will not compile.
 *
 * The dashboard only ever displays and compares these values, so it never needs
 * to do arithmetic on them. Where a numeric view is genuinely required — a chart
 * axis, for instance — conversion happens explicitly at the point of use, with
 * the precision loss acknowledged there. See `DecimalPipe` in shared/.
 */

/** A decimal value transported as a string to preserve precision. */
export type DecimalString = string;

/** Execution mode. Enforced at the backend's execution boundary. */
export type TradingMode = 'BACKTEST' | 'PAPER' | 'LIVE';

/** Direction of a trade. */
export type OrderSide = 'BUY' | 'SELL';

/** A risk gate decision. */
export type RiskDecision = 'Approved' | 'Rejected';

/** Self-description returned by every service's `/api/v1/info`. */
export interface ServiceInfo {
  readonly name: string;
  readonly displayName: string;
  readonly version: string;
  readonly description: string;
  readonly environment: string;
  readonly tradingMode: TradingMode;

  /** True when configuration asked for LIVE and the guard downgraded it. */
  readonly liveTradingBlocked: boolean;
  readonly killSwitchEnabled: boolean;
  readonly serverTimeUtc: string;
}

/** One health check result. */
export interface HealthEntry {
  readonly name: string;
  readonly status: string;
  readonly durationMs: number;
  readonly description?: string;
  readonly tags: readonly string[];
}

/** Aggregate health of a service. */
export interface HealthResponse {
  readonly status: string;
  readonly totalDurationMs: number;
  readonly checks: readonly HealthEntry[];
}

/** Lifecycle state of a trading intent. */
export type TradingIntentStatus =
  | 'Created'
  | 'AwaitingRisk'
  | 'RiskApproved'
  | 'RiskRejected'
  | 'Executing'
  | 'Executed'
  | 'Failed'
  | 'Cancelled'
  | 'RiskUnavailable';

/** A trading intent and its workflow state. */
export interface TradingIntent {
  readonly tradingIntentId: string;
  readonly symbol: string;
  readonly side: OrderSide;
  readonly status: TradingIntentStatus;

  /** Quantity requested. An upper bound; risk may approve less. */
  readonly requestedQuantity: DecimalString;

  /** Quantity the risk gate cleared. Never greater than requested. */
  readonly approvedQuantity: DecimalString;

  readonly entryPrice: DecimalString;
  readonly stopLoss?: DecimalString;
  readonly takeProfit?: DecimalString;
  readonly riskCheckId?: string;
  readonly rejectionCodes: readonly string[];
  readonly tradingMode: TradingMode;
  readonly source: 'STRATEGY' | 'AGENT' | 'MANUAL';
  readonly failureReason?: string;
  readonly createdAt: string;
  readonly correlationId: string;
}

/** The deterministic risk limits in force. */
export interface RiskPolicy {
  readonly id: string;
  readonly name: string;
  readonly isDefault: boolean;
  readonly isActive: boolean;
  readonly maxRiskPerTradePercent: DecimalString;
  readonly maxPositionNotional: DecimalString;
  readonly maxDailyLossPercent: DecimalString;
  readonly maxPortfolioExposurePercent: DecimalString;
  readonly maxAssetConcentrationPercent: DecimalString;
  readonly maxOpenPositions: number;
  readonly minConfidencePercent: DecimalString;
  readonly maxVolatilityPercent: DecimalString;
  readonly requireStopLoss: boolean;
  readonly requireTakeProfit: boolean;
  readonly slippageAssumptionPercent: DecimalString;
  readonly takerFeePercent: DecimalString;
  readonly marketDataStalenessThresholdSeconds: number;
  readonly updatedAt: string;
  readonly updatedBy: string;
}

/** Outcome of one named risk check. */
export interface RiskCheck {
  readonly checkName: string;
  readonly passed: boolean;
  readonly code: string;
  readonly detail: string;
  readonly observedValue?: string;
  readonly limitValue?: string;
}

/** A complete risk gate decision. */
export interface RiskEvaluation {
  readonly riskCheckId: string;
  readonly tradingIntentId: string;
  readonly riskPolicyId: string;
  readonly decision: RiskDecision;
  readonly approvedQuantity: DecimalString;
  readonly approvedNotional: DecimalString;
  readonly effectiveEntryPrice: DecimalString;
  readonly riskAmount: DecimalString;
  readonly riskBudget: DecimalString;
  readonly bindingConstraint: string;
  readonly rejectionCodes: readonly string[];
  readonly checks: readonly RiskCheck[];
}

/** A trading proposal from the AI agent platform. Advisory only. */
export interface TradingProposal {
  readonly action: 'BUY' | 'SELL' | 'HOLD';
  readonly symbol: string;
  readonly confidence: DecimalString;
  readonly entry: DecimalString;
  readonly stopLoss?: DecimalString;
  readonly takeProfit?: DecimalString;
  readonly reasonCodes: readonly string[];
  readonly riskNotes: readonly string[];
  readonly contributions: readonly AgentContribution[];
}

/** One agent's contribution to a proposal. */
export interface AgentContribution {
  readonly agentName: string;
  readonly confidence: DecimalString;
  readonly reasonCodes: readonly string[];
}

/** The complete, auditable record of one agent pipeline run. */
export interface AgentRun {
  readonly agentRunId: string;
  readonly symbol: string;
  readonly timeframe: string;
  readonly startedAt: string;
  readonly completedAt: string;
  readonly durationMs: number;
  readonly model: string;
  readonly toolsUsed: readonly string[];
  readonly analyses: Record<string, Record<string, unknown>>;
  readonly proposal?: TradingProposal;
  readonly succeeded: boolean;
  readonly failureReason?: string;
  readonly inputTokens: number;
  readonly outputTokens: number;
  readonly correlationId: string;
}

/** The AI platform's disclosed tool permissions. */
export interface AgentPermissions {
  readonly registeredTools: readonly string[];
  readonly allowedReadOnlyTools: readonly string[];
  readonly forbiddenTools: readonly string[];
  readonly note: string;
}
