import { Injectable, computed, inject, signal } from '@angular/core';
import { Subscription, forkJoin, interval, startWith, switchMap } from 'rxjs';

import { environment } from '../../../environments/environment';
import { ServiceInfo, TradingMode } from '../models/platform.models';
import { PlatformApiService } from './platform-api.service';

/** A service's gateway route and display name. */
export interface MonitoredService {
  readonly route: string;
  readonly displayName: string;
}

/** The observed state of one service. */
export interface ServiceStatus {
  readonly route: string;
  readonly displayName: string;
  readonly reachable: boolean;
  readonly info: ServiceInfo | null;
}

/**
 * Polls every service's `/api/v1/info` and exposes the result as signals.
 *
 * The platform's fourteen services each report their own effective trading mode
 * and kill-switch state. Aggregating them here lets the dashboard show a single
 * honest answer — and, more usefully, show *disagreement*: if one service
 * believes it is in PAPER while another believes LIVE, that is a configuration
 * fault an operator needs to see immediately rather than discover from a trade.
 */
@Injectable({ providedIn: 'root' })
export class PlatformStatusService {
  private readonly api = inject(PlatformApiService);

  /** The services the dashboard monitors, in display order. */
  static readonly Services: readonly MonitoredService[] = [
    { route: 'auth', displayName: 'Identity' },
    { route: 'market', displayName: 'Market Data' },
    { route: 'trading', displayName: 'Trading' },
    { route: 'risk', displayName: 'Risk' },
    { route: 'execution', displayName: 'Execution' },
    { route: 'portfolio', displayName: 'Portfolio' },
    { route: 'strategies', displayName: 'Strategy' },
    { route: 'agents', displayName: 'AI Agents' },
    { route: 'backtesting', displayName: 'Backtesting' },
    { route: 'reconciliation', displayName: 'Reconciliation' },
    { route: 'notifications', displayName: 'Notification' },
    { route: 'audit', displayName: 'Audit' },
    { route: 'admin', displayName: 'Configuration' },
  ];

  private readonly statusesSignal = signal<readonly ServiceStatus[]>([]);
  private readonly loadingSignal = signal(true);
  private readonly lastUpdatedSignal = signal<Date | null>(null);
  private subscription?: Subscription;

  /** Current status of every monitored service. */
  readonly statuses = this.statusesSignal.asReadonly();

  /** True until the first poll completes. */
  readonly loading = this.loadingSignal.asReadonly();

  /** When the last poll completed. */
  readonly lastUpdated = this.lastUpdatedSignal.asReadonly();

  /** Services that responded. */
  readonly reachableCount = computed(
    () => this.statusesSignal().filter((s) => s.reachable).length,
  );

  readonly totalCount = computed(() => this.statusesSignal().length);

  /**
   * The platform's effective trading mode.
   *
   * Reported as the most permissive mode any service claims, because that is
   * the one that could actually place an order. Taking the *safest* answer
   * would be reassuring and wrong.
   */
  readonly tradingMode = computed<TradingMode | 'UNKNOWN'>(() => {
    const modes = this.statusesSignal()
      .map((s) => s.info?.tradingMode)
      .filter((m): m is TradingMode => !!m);

    if (modes.length === 0) {
      return 'UNKNOWN';
    }

    if (modes.includes('LIVE')) {
      return 'LIVE';
    }

    if (modes.includes('PAPER')) {
      return 'PAPER';
    }

    return 'BACKTEST';
  });

  /**
   * True when services disagree about the trading mode.
   *
   * A configuration fault worth surfacing prominently: it means part of the
   * platform may behave differently from what an operator believes.
   */
  readonly tradingModeInconsistent = computed(() => {
    const modes = new Set(
      this.statusesSignal()
        .map((s) => s.info?.tradingMode)
        .filter((m): m is TradingMode => !!m),
    );

    return modes.size > 1;
  });

  /** True when any service reports the kill switch engaged. */
  readonly killSwitchEngaged = computed(() =>
    this.statusesSignal().some((s) => s.info?.killSwitchEnabled === true),
  );

  /** True when any service had LIVE requested but blocked by the guard. */
  readonly liveTradingBlocked = computed(() =>
    this.statusesSignal().some((s) => s.info?.liveTradingBlocked === true),
  );

  /** Starts polling. Idempotent. */
  start(): void {
    if (this.subscription) {
      return;
    }

    this.subscription = interval(environment.healthPollIntervalMs)
      .pipe(
        startWith(0),
        switchMap(() =>
          forkJoin(
            PlatformStatusService.Services.map((service) =>
              this.api.getServiceInfo(service.route),
            ),
          ),
        ),
      )
      .subscribe((results) => {
        this.statusesSignal.set(
          PlatformStatusService.Services.map((service, index) => ({
            route: service.route,
            displayName: service.displayName,
            reachable: results[index] !== null,
            info: results[index],
          })),
        );

        this.loadingSignal.set(false);
        this.lastUpdatedSignal.set(new Date());
      });
  }

  /** Stops polling. */
  stop(): void {
    this.subscription?.unsubscribe();
    this.subscription = undefined;
  }
}
