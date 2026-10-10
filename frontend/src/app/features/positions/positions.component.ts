import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { PlatformApiService } from '../../core/services/platform-api.service';
import { DEFAULT_TRADING_ACCOUNT_ID, Position } from '../../core/models/platform.models';

/**
 * Open positions: size, weighted average entry, and realised and unrealised
 * P&L, built from order fill events and marked against a live price.
 *
 * Real as of Phase 6. See the remarks on `DEFAULT_TRADING_ACCOUNT_ID` for why
 * every position here is read against one fixed, caller-supplied account id
 * rather than a logged-in user's own account — there is no Identity Service
 * behind this yet.
 */
@Component({
  selector: 'app-positions',
  imports: [DatePipe, RouterLink],
  templateUrl: './positions.component.html',
})
export class PositionsComponent implements OnInit {
  private readonly api = inject(PlatformApiService);

  private readonly positionsSignal = signal<readonly Position[]>([]);
  private readonly loadingSignal = signal(true);

  readonly positions = this.positionsSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();

  ngOnInit(): void {
    this.api.listPositions(DEFAULT_TRADING_ACCOUNT_ID).subscribe((positions) => {
      this.positionsSignal.set(positions);
      this.loadingSignal.set(false);
    });
  }

  /** Maps a P&L figure's sign onto a badge class. Zero reads as neutral, not a loss. */
  pnlClass(value: string): string {
    const parsed = Number(value);
    if (Number.isNaN(parsed) || parsed === 0) {
      return 'numeric';
    }
    return parsed > 0 ? 'numeric pnl--positive' : 'numeric pnl--negative';
  }
}
