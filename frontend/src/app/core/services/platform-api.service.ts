import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, of } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  AgentPermissions,
  AgentRun,
  HealthResponse,
  Position,
  PortfolioSnapshot,
  RiskPolicy,
  ServiceInfo,
  TradingIntent,
} from '../models/platform.models';

/**
 * The dashboard's single HTTP client.
 *
 * Every request goes through the API gateway. The dashboard never addresses a
 * service directly, so authentication, rate limiting and CORS are enforced in
 * one place and the browser needs exactly one origin allow-listed.
 */
@Injectable({ providedIn: 'root' })
export class PlatformApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiBaseUrl;

  // --- Platform -----------------------------------------------------------

  /** Reads one service's self-description through the gateway. */
  getServiceInfo(route: string): Observable<ServiceInfo | null> {
    return this.http
      .get<ServiceInfo>(`${this.baseUrl}/${route}/api/v1/info`)
      .pipe(catchError(() => of(null)));
  }

  /** Reads the gateway's own health. */
  getGatewayHealth(): Observable<HealthResponse | null> {
    return this.http
      .get<HealthResponse>(`${this.baseUrl}/health`)
      .pipe(catchError(() => of(null)));
  }

  /** Reads one service's detailed health through the gateway. */
  getServiceHealth(route: string): Observable<HealthResponse | null> {
    return this.http
      .get<HealthResponse>(`${this.baseUrl}/${route}/health`)
      .pipe(catchError(() => of(null)));
  }

  // --- Trading ------------------------------------------------------------

  /** Lists recent trading intents, newest first. */
  listTradingIntents(limit = 50): Observable<readonly TradingIntent[]> {
    return this.http
      .get<TradingIntent[]>(`${this.baseUrl}/trading/intents`, { params: { limit } })
      .pipe(catchError(this.emptyList<TradingIntent>()));
  }

  /** Reads one trading intent. */
  getTradingIntent(id: string): Observable<TradingIntent | null> {
    return this.http
      .get<TradingIntent>(`${this.baseUrl}/trading/intents/${id}`)
      .pipe(catchError(() => of(null)));
  }

  // --- Risk ---------------------------------------------------------------

  /** Reads the active default risk policy. */
  getDefaultRiskPolicy(): Observable<RiskPolicy | null> {
    return this.http
      .get<RiskPolicy>(`${this.baseUrl}/risk/policies/default`)
      .pipe(catchError(() => of(null)));
  }

  /** Lists the configured risk policies. */
  listRiskPolicies(): Observable<readonly RiskPolicy[]> {
    return this.http
      .get<RiskPolicy[]>(`${this.baseUrl}/risk/policies`)
      .pipe(catchError(this.emptyList<RiskPolicy>()));
  }

  // --- Portfolio ------------------------------------------------------------

  /**
   * Lists open positions for a trading account.
   *
   * The platform has no account/identity concept yet — every intent and
   * every position is keyed by a caller-supplied `tradingAccountId` with no
   * login behind it. `DEFAULT_TRADING_ACCOUNT_ID` is the convention this
   * dashboard and the test/demo tooling both use so a position shows up
   * here at all; see docs/architecture/known-limitations.md.
   */
  listPositions(tradingAccountId: string): Observable<readonly Position[]> {
    return this.http
      .get<Position[]>(`${this.baseUrl}/portfolio/positions`, { params: { tradingAccountId } })
      .pipe(catchError(this.emptyList<Position>()));
  }

  /** Reads an account's equity, available cash, exposure and today's P&L. */
  getPortfolioSnapshot(tradingAccountId: string, symbol: string): Observable<PortfolioSnapshot | null> {
    return this.http
      .get<PortfolioSnapshot>(`${this.baseUrl}/portfolio/snapshot`, { params: { tradingAccountId, symbol } })
      .pipe(catchError(() => of(null)));
  }

  // --- AI agents ----------------------------------------------------------

  /**
   * Reads the AI platform's disclosed tool permissions.
   *
   * Surfaced in the dashboard so an operator can confirm the AI trust boundary
   * from the running system rather than from documentation that may have
   * drifted from the code.
   */
  getAgentPermissions(): Observable<AgentPermissions | null> {
    return this.http
      .get<AgentPermissions>(`${this.baseUrl}/agents/api/v1/agents/permissions`)
      .pipe(catchError(() => of(null)));
  }

  /** Runs the agent pipeline for a symbol and returns the auditable record. */
  runAgentPipeline(symbol: string, timeframe = '15m'): Observable<AgentRun | null> {
    return this.http
      .post<AgentRun>(`${this.baseUrl}/agents/api/v1/agents/runs`, { symbol, timeframe })
      .pipe(catchError(() => of(null)));
  }

  /**
   * Returns an empty list on failure.
   *
   * A dashboard panel that cannot load its data should render empty with the
   * rest of the page intact, not take down the whole view. Genuine errors are
   * still visible: the platform status page reports each service's health
   * directly, so a failing dependency is shown there rather than inferred from
   * a blank table.
   */
  private emptyList<T>(): (error: HttpErrorResponse) => Observable<readonly T[]> {
    return () => of([] as readonly T[]);
  }
}
