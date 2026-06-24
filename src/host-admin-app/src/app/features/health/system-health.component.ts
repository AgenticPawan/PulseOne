import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, combineLatest, interval, map, of, startWith, switchMap } from 'rxjs';
import { HealthReport, HealthState, QueueDepth } from '../../core/models/host-models';
import { StatusBadgeComponent } from '../../shared/status-badge.component';

interface HealthSnapshot {
  readonly ready: HealthReport | null;
  readonly live: HealthState;
  readonly queue: QueueDepth | null;
  readonly fetchedAt: Date;
  readonly reachable: boolean;
}

/**
 * System health dashboard. Polls health + worker-queue endpoints every 30s using the rxjs
 * `interval` + `switchMap` pattern (prompt requires this explicitly — NOT `effect`). The polling
 * stream is bridged to a signal via `toSignal` so the zoneless template renders reactively.
 *
 * Endpoints:
 *   GET /health/ready  (per-dependency checks: API, Hangfire DB, Tenant Catalog DB, Key Vault)
 *   GET /health/live   (liveness)
 *   GET /api/v1/host/system/queue-depth  (Hangfire enqueued/processing/failed)
 */
@Component({
  selector: 'po-system-health',
  standalone: true,
  imports: [StatusBadgeComponent, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section aria-labelledby="health-heading" class="space-y-6">
      <div class="flex items-center justify-between">
        <h2 id="health-heading" class="text-lg font-semibold">System Health</h2>
        @if (snapshot(); as s) {
          <span class="text-xs text-slate-500">
            Last updated {{ s.fetchedAt | date: 'mediumTime' }} · auto-refresh every 30s
          </span>
        }
      </div>

      @if (snapshot(); as s) {
        @if (!s.reachable) {
          <p class="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700" role="alert">
            Unable to reach the API health endpoints. The API may be down or the request was blocked.
          </p>
        }

        <!-- Liveness / readiness summary -->
        <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div class="rounded-lg border border-slate-200 bg-white p-4">
            <p class="text-sm text-slate-500">Liveness</p>
            <div class="mt-2"><po-status-badge [status]="s.live" /></div>
          </div>
          <div class="rounded-lg border border-slate-200 bg-white p-4">
            <p class="text-sm text-slate-500">Overall readiness</p>
            <div class="mt-2"><po-status-badge [status]="s.ready?.status ?? 'Unhealthy'" /></div>
          </div>
        </div>

        <!-- Per-dependency checks -->
        <div class="overflow-hidden rounded-lg border border-slate-200 bg-white">
          <table role="grid" class="min-w-full divide-y divide-slate-200 text-sm">
            <caption class="sr-only">Dependency health checks</caption>
            <thead class="bg-slate-50">
              <tr>
                <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Dependency</th>
                <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Status</th>
                <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Detail</th>
                <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Latency</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-100">
              @for (c of s.ready?.checks ?? []; track c.name) {
                <tr>
                  <th scope="row" class="px-4 py-2 text-left font-medium">{{ c.name }}</th>
                  <td class="px-4 py-2"><po-status-badge [status]="c.status" /></td>
                  <td class="px-4 py-2 text-slate-600">{{ c.description ?? '—' }}</td>
                  <td class="px-4 py-2 text-slate-500">{{ c.durationMs }} ms</td>
                </tr>
              } @empty {
                <tr><td colspan="4" class="px-4 py-6 text-center text-slate-500">No dependency checks reported.</td></tr>
              }
            </tbody>
          </table>
        </div>

        <!-- Worker queue depth -->
        <div>
          <h3 class="mb-2 text-sm font-semibold text-slate-700">Worker queue depth (Hangfire)</h3>
          <div class="grid grid-cols-2 gap-4 lg:grid-cols-4">
            <div class="rounded-lg border border-slate-200 bg-white p-4">
              <p class="text-sm text-slate-500">Enqueued</p>
              <p class="mt-1 text-2xl font-semibold">{{ s.queue?.enqueued ?? '—' }}</p>
            </div>
            <div class="rounded-lg border border-slate-200 bg-white p-4">
              <p class="text-sm text-slate-500">Processing</p>
              <p class="mt-1 text-2xl font-semibold">{{ s.queue?.processing ?? '—' }}</p>
            </div>
            <div class="rounded-lg border border-slate-200 bg-white p-4">
              <p class="text-sm text-slate-500">Failed</p>
              <p class="mt-1 text-2xl font-semibold text-red-600">{{ s.queue?.failed ?? '—' }}</p>
            </div>
            <div class="rounded-lg border border-slate-200 bg-white p-4">
              <p class="text-sm text-slate-500">Succeeded</p>
              <p class="mt-1 text-2xl font-semibold text-emerald-600">{{ s.queue?.succeeded ?? '—' }}</p>
            </div>
          </div>
        </div>
      } @else {
        <p class="text-sm text-slate-500" role="status">Loading system health…</p>
      }
    </section>
  `,
})
export class SystemHealthComponent {
  private readonly http = inject(HttpClient);

  // interval + switchMap polling (NOT effect). Each 30s tick fans out to all three reads; failures
  // degrade gracefully so one unreachable dependency never blanks the whole dashboard.
  private readonly poll$ = interval(30_000).pipe(
    startWith(0),
    switchMap(() =>
      combineLatest({
        ready: this.http
          .get<HealthReport>('/health/ready')
          .pipe(catchError(() => of(null))),
        live: this.http
          .get<{ status?: string }>('/health/live')
          .pipe(
            map((r) => this.toState(r?.status)),
            catchError(() => of<HealthState>('Unhealthy')),
          ),
        queue: this.http
          .get<QueueDepth>('/api/v1/host/system/queue-depth')
          .pipe(catchError(() => of(null))),
      }).pipe(
        map(
          (r): HealthSnapshot => ({
            ...r,
            fetchedAt: new Date(),
            reachable: r.ready !== null || r.live !== 'Unhealthy',
          }),
        ),
      ),
    ),
  );

  protected readonly snapshot = toSignal(this.poll$, { initialValue: null });

  private toState(raw: string | undefined): HealthState {
    const s = (raw ?? '').toLowerCase();
    if (s === 'healthy' || s === 'ok' || s === 'up') return 'Healthy';
    if (s === 'degraded') return 'Degraded';
    return 'Unhealthy';
  }
}
