import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ActivityEntry, DashboardSummary } from '../../core/models/tenant-models';
import { FileSizePipe } from '../../shared/file-size.pipe';

/**
 * Tenant dashboard (prompt 01): KPI cards (Active Users, Reports Generated, Storage Used, Current
 * Plan) and a recent-activity feed (last 10 audit events for the current user).
 *
 * Both reads are Angular 20 `httpResource` (NOT effect()+subscribe). A `refreshTick` signal is
 * incremented every 60s by a plain setInterval; the request fns read it, so the resources re-fetch
 * on the timer AND cancel any in-flight request. The interval is cleared on destroy via DestroyRef.
 * No `effect()` is used to drive HTTP (the v1 anti-pattern, Appendix A #7).
 */
@Component({
  selector: 'po-dashboard',
  standalone: true,
  imports: [DatePipe, FileSizePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="space-y-6" aria-labelledby="dashboard-heading">
      <h2 id="dashboard-heading" class="text-lg font-semibold text-slate-900">Dashboard</h2>

      <!-- KPI cards -->
      <div aria-live="polite" class="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        @if (summary.isLoading()) {
          <p class="col-span-full text-sm text-slate-500" role="status">Loading summary…</p>
        } @else if (summary.error()) {
          <p class="col-span-full text-sm text-red-600" role="alert">
            Could not load dashboard summary.
          </p>
        } @else if (summary.value(); as s) {
          <article class="rounded-xl border border-slate-200 bg-white p-4">
            <p class="text-xs font-medium uppercase tracking-wide text-slate-500">Active Users</p>
            <p class="mt-2 text-2xl font-semibold text-slate-900">{{ s.activeUsers }}</p>
          </article>
          <article class="rounded-xl border border-slate-200 bg-white p-4">
            <p class="text-xs font-medium uppercase tracking-wide text-slate-500">
              Reports Generated
            </p>
            <p class="mt-2 text-2xl font-semibold text-slate-900">{{ s.reportsGenerated }}</p>
          </article>
          <article class="rounded-xl border border-slate-200 bg-white p-4">
            <p class="text-xs font-medium uppercase tracking-wide text-slate-500">Storage Used</p>
            <p class="mt-2 text-2xl font-semibold text-slate-900">
              {{ s.storageUsedBytes | fileSize }}
            </p>
            <p class="mt-1 text-xs text-slate-500">of {{ s.storageQuotaBytes | fileSize }}</p>
          </article>
          <article class="rounded-xl border border-slate-200 bg-white p-4">
            <p class="text-xs font-medium uppercase tracking-wide text-slate-500">Current Plan</p>
            <p class="mt-2 text-2xl font-semibold text-slate-900">{{ s.currentPlan }}</p>
          </article>
        }
      </div>

      <!-- Recent activity -->
      <div class="rounded-xl border border-slate-200 bg-white">
        <div class="border-b border-slate-200 px-4 py-3">
          <h3 class="text-sm font-semibold text-slate-900">Recent activity</h3>
        </div>
        <div aria-live="polite">
          @if (activity.isLoading()) {
            <p class="px-4 py-6 text-sm text-slate-500" role="status">Loading activity…</p>
          } @else if (activity.error()) {
            <p class="px-4 py-6 text-sm text-red-600" role="alert">Could not load activity.</p>
          } @else {
            <ul class="divide-y divide-slate-100">
              @for (item of activity.value() ?? []; track item.id) {
                <li class="flex items-start justify-between gap-4 px-4 py-3">
                  <div class="min-w-0">
                    <p class="text-sm font-medium text-slate-800">{{ item.action }}</p>
                    <p class="truncate text-sm text-slate-500">{{ item.summary }}</p>
                    <p class="text-xs text-slate-400">{{ item.actorEmail }}</p>
                  </div>
                  <time
                    [attr.datetime]="item.timestampUtc"
                    class="shrink-0 text-xs text-slate-400"
                  >
                    {{ item.timestampUtc | date: 'short' }}
                  </time>
                </li>
              } @empty {
                <li class="px-4 py-6 text-center text-sm text-slate-500">No recent activity.</li>
              }
            </ul>
          }
        </div>
      </div>
    </section>
  `,
})
export class DashboardComponent {
  /** Incremented every 60s to drive a reactive refetch of both resources. */
  private readonly refreshTick = signal(0);

  constructor() {
    const intervalId = setInterval(() => this.refreshTick.update((n) => n + 1), 60_000);
    inject(DestroyRef).onDestroy(() => clearInterval(intervalId));
  }

  protected readonly summary = httpResource<DashboardSummary>(() => {
    this.refreshTick();
    return { url: '/api/v1/dashboard/summary' };
  });

  protected readonly activity = httpResource<readonly ActivityEntry[]>(() => {
    this.refreshTick();
    return { url: '/api/v1/dashboard/activity', params: { take: 10 } };
  });

  // Exposed for potential header display; derived from the summary resource.
  protected readonly planLabel = computed(() => this.summary.value()?.currentPlan ?? '');
}
