import { Component, OnInit, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { PlatformStatusService } from './core/services/platform-status.service';

/** One navigation entry. */
interface NavItem {
  readonly path: string;
  readonly label: string;
  readonly icon: string;
}

/**
 * The dashboard shell: navigation, and the platform-wide status banner.
 *
 * The banner is in the shell rather than on a single page on purpose. The
 * trading mode and the kill-switch state change what every other screen means,
 * and an operator must never have to navigate somewhere to find out whether the
 * platform is live.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  private readonly status = inject(PlatformStatusService);

  readonly navItems: readonly NavItem[] = [
    { path: '/dashboard', label: 'Dashboard', icon: 'grid' },
    { path: '/trading', label: 'Trading', icon: 'activity' },
    { path: '/orders', label: 'Orders', icon: 'list' },
    { path: '/positions', label: 'Positions', icon: 'layers' },
    { path: '/market', label: 'Market', icon: 'trending' },
    { path: '/signals', label: 'Signals', icon: 'zap' },
    { path: '/agents', label: 'AI Agents', icon: 'cpu' },
    { path: '/strategies', label: 'Strategies', icon: 'git' },
    { path: '/backtesting', label: 'Backtesting', icon: 'rewind' },
    { path: '/risk', label: 'Risk', icon: 'shield' },
    { path: '/audit', label: 'Audit', icon: 'file' },
    { path: '/settings', label: 'Settings', icon: 'sliders' },
    { path: '/administration', label: 'Administration', icon: 'tool' },
  ];

  readonly tradingMode = this.status.tradingMode;
  readonly killSwitchEngaged = this.status.killSwitchEngaged;
  readonly liveTradingBlocked = this.status.liveTradingBlocked;
  readonly tradingModeInconsistent = this.status.tradingModeInconsistent;
  readonly reachableCount = this.status.reachableCount;
  readonly totalCount = this.status.totalCount;
  readonly loading = this.status.loading;

  /** Banner severity, which drives its colour. */
  readonly bannerSeverity = computed<'critical' | 'warning' | 'info' | 'ok'>(() => {
    if (this.killSwitchEngaged() || this.tradingModeInconsistent()) {
      return 'critical';
    }

    if (this.tradingMode() === 'LIVE') {
      // Live trading is not a fault, but it must never look like an ordinary
      // state. Real money is at risk whenever this is showing.
      return 'warning';
    }

    if (this.liveTradingBlocked() || this.tradingMode() === 'UNKNOWN') {
      return 'info';
    }

    return 'ok';
  });

  readonly bannerMessage = computed(() => {
    if (this.killSwitchEngaged()) {
      return 'Kill switch ENGAGED — no new orders are being admitted.';
    }

    if (this.tradingModeInconsistent()) {
      return 'Services disagree about the trading mode. Investigate before trading.';
    }

    if (this.liveTradingBlocked()) {
      return 'LIVE trading was requested but is blocked by configuration. Running in PAPER.';
    }

    if (this.tradingMode() === 'LIVE') {
      return 'LIVE trading — real orders with real funds.';
    }

    if (this.tradingMode() === 'UNKNOWN') {
      return 'Trading mode unknown — no service has reported yet.';
    }

    return 'PAPER trading — simulated fills, no real funds at risk.';
  });

  ngOnInit(): void {
    this.status.start();
  }
}
