import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import {
  PagedResult,
  SubscriptionMetrics,
  SubscriptionSummary,
} from '../../core/models/host-models';
import { HostApiService } from '../../core/services/host-api.service';
import { StatusBadgeComponent } from '../../shared/status-badge.component';

/**
 * Subscription & billing dashboard. Overview metric cards + a paged table of every subscription
 * across all tenants, with manual operator overrides (extend trial, apply discount, cancel).
 *
 * Reads use `httpResource` (constraint #5); overrides go through `HostApiService` which bumps
 * `mutationVersion`, re-fetching both the metrics and the table. Endpoints (HostOperatorsOnly):
 *   GET  /api/v1/host/subscriptions/metrics
 *   GET  /api/v1/host/subscriptions
 *   POST /api/v1/host/subscriptions/{id}/extend-trial | /discount | /cancel
 */
@Component({
  selector: 'po-subscription-dashboard',
  standalone: true,
  imports: [DatePipe, StatusBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section aria-labelledby="subs-dash-heading" class="space-y-6">
      <h2 id="subs-dash-heading" class="text-lg font-semibold">Subscriptions &amp; Billing</h2>

      <!-- Metric cards -->
      <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        @if (metrics.isLoading()) {
          <p class="col-span-full text-sm text-slate-500" role="status">Loading metrics…</p>
        } @else if (metrics.error()) {
          <p class="col-span-full text-sm text-red-600" role="alert">Failed to load billing metrics.</p>
        } @else if (metrics.value(); as m) {
          <div class="rounded-lg border border-slate-200 bg-white p-4">
            <p class="text-sm text-slate-500">Active subscriptions</p>
            <p class="mt-1 text-2xl font-semibold">{{ m.activeSubscriptions }}</p>
          </div>
          <div class="rounded-lg border border-slate-200 bg-white p-4">
            <p class="text-sm text-slate-500">MRR</p>
            <p class="mt-1 text-2xl font-semibold">{{ rupees(m.monthlyRecurringRevenueInPaise) }}</p>
          </div>
          <div class="rounded-lg border border-slate-200 bg-white p-4">
            <p class="text-sm text-slate-500">Churn rate</p>
            <p class="mt-1 text-2xl font-semibold">{{ m.churnRatePercent }}%</p>
          </div>
          <div class="rounded-lg border border-slate-200 bg-white p-4">
            <p class="text-sm text-slate-500">Pending cancellations</p>
            <p class="mt-1 text-2xl font-semibold">{{ m.pendingCancellations }}</p>
          </div>
        }
      </div>

      <!-- Subscriptions table -->
      <div class="overflow-hidden rounded-lg border border-slate-200 bg-white">
        @if (subscriptions.isLoading()) {
          <p class="p-6 text-sm text-slate-500" role="status">Loading subscriptions…</p>
        } @else if (subscriptions.error()) {
          <p class="p-6 text-sm text-red-600" role="alert">Failed to load subscriptions.</p>
        } @else {
          <table role="grid" class="min-w-full divide-y divide-slate-200 text-sm">
            <caption class="sr-only">All platform subscriptions</caption>
            <thead class="bg-slate-50">
              <tr>
                <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Tenant</th>
                <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Razorpay subscription</th>
                <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Plan</th>
                <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Status</th>
                <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Next billing</th>
                <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Amount</th>
                <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Overrides</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-100">
              @for (s of subscriptions.value()?.items ?? []; track s.razorpaySubscriptionId) {
                <tr class="hover:bg-slate-50">
                  <th scope="row" class="px-4 py-2 text-left font-normal">
                    {{ s.tenantName }}
                    <span class="block font-mono text-xs text-slate-400">{{ s.tenantId }}</span>
                  </th>
                  <td class="px-4 py-2 font-mono text-xs">{{ s.razorpaySubscriptionId }}</td>
                  <td class="px-4 py-2">{{ s.plan }}</td>
                  <td class="px-4 py-2"><po-status-badge [status]="s.status" /></td>
                  <td class="px-4 py-2 text-slate-500">
                    {{ s.nextBillingUtc ? (s.nextBillingUtc | date: 'mediumDate') : '—' }}
                  </td>
                  <td class="px-4 py-2">{{ rupees(s.amountInPaise) }}</td>
                  <td class="px-4 py-2">
                    <div class="flex flex-wrap items-center gap-2">
                      <button
                        type="button"
                        (click)="extendTrial(s)"
                        [disabled]="busy() === s.razorpaySubscriptionId"
                        class="text-indigo-600 hover:underline disabled:opacity-50"
                        [attr.aria-label]="'Extend trial for ' + s.tenantName"
                      >
                        Extend trial
                      </button>
                      <button
                        type="button"
                        (click)="applyDiscount(s)"
                        [disabled]="busy() === s.razorpaySubscriptionId"
                        class="text-indigo-600 hover:underline disabled:opacity-50"
                        [attr.aria-label]="'Apply discount for ' + s.tenantName"
                      >
                        Discount
                      </button>
                      @if (s.status !== 'cancelled') {
                        <button
                          type="button"
                          (click)="cancel(s)"
                          [disabled]="busy() === s.razorpaySubscriptionId"
                          class="text-red-600 hover:underline disabled:opacity-50"
                          [attr.aria-label]="'Cancel subscription for ' + s.tenantName"
                        >
                          Cancel
                        </button>
                      }
                    </div>
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="7" class="px-4 py-6 text-center text-slate-500">No subscriptions.</td></tr>
              }
            </tbody>
          </table>

          <div class="flex items-center justify-between border-t border-slate-200 px-4 py-3 text-sm">
            <span class="text-slate-500">{{ rangeLabel() }}</span>
            <div class="flex gap-2">
              <button type="button" (click)="prev()" [disabled]="page() <= 1"
                class="rounded-md border border-slate-300 px-3 py-1 disabled:opacity-50" aria-label="Previous page">Previous</button>
              <button type="button" (click)="next()" [disabled]="!hasNext()"
                class="rounded-md border border-slate-300 px-3 py-1 disabled:opacity-50" aria-label="Next page">Next</button>
            </div>
          </div>
        }
      </div>
    </section>
  `,
})
export class SubscriptionDashboardComponent {
  private readonly api = inject(HostApiService);

  protected readonly page = signal(1);
  protected readonly pageSize = 20;
  protected readonly busy = signal<string | null>(null);

  protected readonly metrics = httpResource<SubscriptionMetrics>(() => {
    this.api.mutationVersion();
    return { url: '/api/v1/host/subscriptions/metrics' };
  });

  protected readonly subscriptions = httpResource<PagedResult<SubscriptionSummary>>(() => {
    this.api.mutationVersion();
    return {
      url: '/api/v1/host/subscriptions',
      params: { pageNumber: this.page(), pageSize: this.pageSize },
    };
  });

  protected readonly hasNext = computed(() => {
    const r = this.subscriptions.value();
    return r ? r.pageNumber * this.pageSize < r.totalCount : false;
  });

  protected readonly rangeLabel = computed(() => {
    const r = this.subscriptions.value();
    if (!r || r.totalCount === 0) return 'No results';
    const start = (r.pageNumber - 1) * this.pageSize + 1;
    const end = Math.min(r.pageNumber * this.pageSize, r.totalCount);
    return `${start}–${end} of ${r.totalCount}`;
  });

  protected rupees(paise: number): string {
    return new Intl.NumberFormat('en-IN', {
      style: 'currency',
      currency: 'INR',
      maximumFractionDigits: 0,
    }).format(paise / 100);
  }

  protected prev(): void {
    this.page.update((p) => Math.max(1, p - 1));
  }

  protected next(): void {
    if (this.hasNext()) this.page.update((p) => p + 1);
  }

  protected async extendTrial(s: SubscriptionSummary): Promise<void> {
    const input = window.prompt('Extend trial by how many days?', '14');
    const days = input ? Number(input) : NaN;
    if (!Number.isFinite(days) || days <= 0) return;
    await this.run(s, () => this.api.extendTrial(s.razorpaySubscriptionId, days));
  }

  protected async applyDiscount(s: SubscriptionSummary): Promise<void> {
    const input = window.prompt('Discount percent (1-100)?', '10');
    const percent = input ? Number(input) : NaN;
    if (!Number.isFinite(percent) || percent <= 0 || percent > 100) return;
    await this.run(s, () => this.api.applyDiscount(s.razorpaySubscriptionId, percent));
  }

  protected async cancel(s: SubscriptionSummary): Promise<void> {
    if (!window.confirm(`Cancel subscription for ${s.tenantName}? This cannot be undone here.`)) {
      return;
    }
    await this.run(s, () => this.api.cancelSubscription(s.razorpaySubscriptionId));
  }

  private async run(s: SubscriptionSummary, op: () => Promise<void>): Promise<void> {
    this.busy.set(s.razorpaySubscriptionId);
    try {
      await op();
    } finally {
      this.busy.set(null);
    }
  }
}
