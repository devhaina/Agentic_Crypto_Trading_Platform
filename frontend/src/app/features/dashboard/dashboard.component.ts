import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';

import { PlatformApiService } from '../../core/services/platform-api.service';
import {
  PlatformStatusService,
  ServiceStatus,
} from '../../core/services/platform-status.service';
import { RiskPolicy, TradingIntent } from '../../core/models/platform.models';

/**
 * The operational overview.
 *
 * Phase 1 shows what the platform can actually report: live service health, the
 * effective trading mode, the risk limits in force, and recent trading intents.
 * The portfolio value and P&L panels are present but explicitly marked as
 * awaiting Phase 6 rather than filled with placeholder numbers — a dashboard
 * that displays a plausible fabricated balance is worse than one that admits it
 * has no data, because someone will eventually act on it.
 */
@Component({
  selector: 'app-dashboard',
  imports: [DatePipe, RouterLink],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit {
  private readonly api = inject(PlatformApiService);
  private readonly status = inject(PlatformStatusService);

  readonly statuses = this.status.statuses;
  readonly loading = this.status.loading;
  readonly lastUpdated = this.status.lastUpdated;
  readonly tradingMode = this.status.tradingMode;
  readonly killSwitchEngaged = this.status.killSwitchEngaged;

  private readonly intentsSignal = signal<readonly TradingIntent[]>([]);
  private readonly policySignal = signal<RiskPolicy | null>(null);

  readonly recentIntents = this.intentsSignal.asReadonly();
  readonly riskPolicy = this.policySignal.asReadonly();

  readonly healthyServices = computed(() =>
    this.statuses().filter((s: ServiceStatus) => s.reachable),
  );

  readonly unhealthyServices = computed(() =>
    this.statuses().filter((s: ServiceStatus) => !s.reachable),
  );

  readonly approvedIntentCount = computed(
    () => this.intentsSignal().filter((i) => i.status === 'RiskApproved').length,
  );

  readonly rejectedIntentCount = computed(
    () => this.intentsSignal().filter((i) => i.status === 'RiskRejected').length,
  );

  ngOnInit(): void {
    this.status.start();

    this.api.listTradingIntents(10).subscribe((intents) => this.intentsSignal.set(intents));
    this.api.getDefaultRiskPolicy().subscribe((policy) => this.policySignal.set(policy));
  }

  /** Maps an intent status onto a badge class. */
  statusBadge(status: TradingIntent['status']): string {
    switch (status) {
      case 'Executed':
      case 'RiskApproved':
        return 'badge badge--ok';
      case 'RiskRejected':
        return 'badge badge--warn';
      case 'Failed':
      case 'RiskUnavailable':
        return 'badge badge--danger';
      default:
        return 'badge badge--muted';
    }
  }
}
