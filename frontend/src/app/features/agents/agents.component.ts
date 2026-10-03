import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { JsonPipe } from '@angular/common';

import { PlatformApiService } from '../../core/services/platform-api.service';
import { AgentPermissions, AgentRun } from '../../core/models/platform.models';

/**
 * AI agent monitoring, and the trust boundary made visible.
 *
 * Two things matter on this page.
 *
 * First, the permission disclosure. It is read live from the agent platform, so
 * an operator can verify from the running system that agents hold only
 * read-only tools — rather than taking a document's word for it, which may have
 * drifted from the code.
 *
 * Second, the run detail. Every agent run records which agents participated,
 * which tools they called, what each concluded and what was proposed, so a
 * trade can be explained after the fact.
 */
@Component({
  selector: 'app-agents',
  imports: [FormsModule, JsonPipe],
  templateUrl: './agents.component.html',
  styleUrl: './agents.component.scss',
})
export class AgentsComponent implements OnInit {
  private readonly api = inject(PlatformApiService);

  private readonly permissionsSignal = signal<AgentPermissions | null>(null);
  private readonly runSignal = signal<AgentRun | null>(null);
  private readonly runningSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  readonly permissions = this.permissionsSignal.asReadonly();
  readonly run = this.runSignal.asReadonly();
  readonly running = this.runningSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  symbol = 'BTCUSDT';
  timeframe = '15m';

  ngOnInit(): void {
    this.api.getAgentPermissions().subscribe((p) => this.permissionsSignal.set(p));
  }

  /** Runs the agent pipeline and shows the full run record. */
  runPipeline(): void {
    this.runningSignal.set(true);
    this.errorSignal.set(null);
    this.runSignal.set(null);

    this.api.runAgentPipeline(this.symbol, this.timeframe).subscribe((result) => {
      this.runningSignal.set(false);

      if (result === null) {
        this.errorSignal.set(
          'The agent platform could not be reached, or the run failed. Check that the ' +
            'agent-platform service is up.',
        );
        return;
      }

      this.runSignal.set(result);
    });
  }

  /** Agent names present in a run, in a stable order. */
  agentNames(run: AgentRun): readonly string[] {
    return Object.keys(run.analyses).sort();
  }
}
