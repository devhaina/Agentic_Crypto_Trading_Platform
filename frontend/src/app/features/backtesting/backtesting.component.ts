import { Component } from '@angular/core';

/**
 * Backtesting.
 *
 * Phase 1 renders the route and states plainly what the page will show and
 * when. The alternative — a page populated with plausible sample figures — is
 * actively harmful in a trading tool, because someone will eventually read a
 * fabricated balance as real.
 */
@Component({
  selector: 'app-backtesting',
  templateUrl: './backtesting.component.html',
})
export class BacktestingComponent {}
