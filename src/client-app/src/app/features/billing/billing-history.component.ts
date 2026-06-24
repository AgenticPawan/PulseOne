import { Component, signal } from '@angular/core';
import { httpResource } from '@angular/common/http';

interface PaymentRow {
  id: string;
  date: string;
  amountInRupees: number;
  status: string;
  invoiceUrl: string | null;
}

interface PagedPayments {
  items: PaymentRow[];
  totalCount: number;
  pageIndex: number;
}

/**
 * Paged history of past payments (prompt 02). Reactive fetch via `httpResource` keyed on `pageIndex`
 * and `dateRange` signals — changing a filter recomputes the request AND cancels the in-flight one
 * (the v1 imperative-subscribe anti-pattern is avoided, Appendix A #7).
 */
@Component({
  selector: 'pulseone-billing-history',
  standalone: true,
  template: `
    <section class="rounded-xl border border-slate-200 bg-white p-5">
      <h2 class="text-lg font-semibold text-slate-900">Payment history</h2>

      <div class="mt-3 flex flex-wrap gap-4 text-sm">
        <label class="flex items-center gap-2 text-slate-700">
          From
          <input
            type="date"
            [value]="fromDate()"
            (change)="onFrom($event)"
            class="rounded-md border border-slate-300 px-2 py-1 focus:border-indigo-500 focus:outline-none"
          />
        </label>
        <label class="flex items-center gap-2 text-slate-700">
          To
          <input
            type="date"
            [value]="toDate()"
            (change)="onTo($event)"
            class="rounded-md border border-slate-300 px-2 py-1 focus:border-indigo-500 focus:outline-none"
          />
        </label>
      </div>

      <div aria-live="polite" class="mt-4">
        @if (history.isLoading()) {
          <p class="text-sm text-slate-500" role="status">Loading…</p>
        } @else if (history.error()) {
          <p class="text-sm text-red-700" role="alert">Could not load payment history.</p>
        } @else {
          <table class="min-w-full divide-y divide-slate-200 text-sm">
            <thead class="bg-slate-50">
              <tr class="text-left text-slate-500">
                <th class="px-3 py-2 font-medium">Date</th>
                <th class="px-3 py-2 font-medium">Amount</th>
                <th class="px-3 py-2 font-medium">Status</th>
                <th class="px-3 py-2 font-medium">Invoice</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-100">
              @for (row of history.value()?.items ?? []; track row.id) {
                <tr>
                  <td class="px-3 py-2 text-slate-600">{{ row.date }}</td>
                  <td class="px-3 py-2 text-slate-800">₹{{ row.amountInRupees }}</td>
                  <td class="px-3 py-2 text-slate-600">{{ row.status }}</td>
                  <td class="px-3 py-2">
                    @if (row.invoiceUrl) {
                      <a
                        [href]="row.invoiceUrl"
                        rel="noopener"
                        target="_blank"
                        class="text-indigo-600 hover:underline"
                        >Download</a
                      >
                    } @else {
                      <span class="text-slate-400">—</span>
                    }
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="4" class="px-3 py-6 text-center text-slate-500">No payments yet.</td>
                </tr>
              }
            </tbody>
          </table>

          <div class="mt-3 flex items-center justify-end gap-2 text-sm">
            <button
              type="button"
              [disabled]="pageIndex() === 0"
              (click)="prev()"
              class="rounded-md border border-slate-300 px-3 py-1 disabled:opacity-50"
            >
              Previous
            </button>
            <span class="text-slate-500">Page {{ pageIndex() + 1 }}</span>
            <button
              type="button"
              (click)="next()"
              class="rounded-md border border-slate-300 px-3 py-1"
            >
              Next
            </button>
          </div>
        }
      </div>
    </section>
  `,
})
export class BillingHistoryComponent {
  protected readonly pageIndex = signal(0);
  protected readonly fromDate = signal('');
  protected readonly toDate = signal('');

  protected readonly history = httpResource<PagedPayments>(() => ({
    url: '/api/v1/billing/history',
    params: {
      pageIndex: this.pageIndex(),
      pageSize: 10,
      from: this.fromDate(),
      to: this.toDate(),
    },
  }));

  protected prev(): void {
    this.pageIndex.update((p) => Math.max(0, p - 1));
  }

  protected next(): void {
    this.pageIndex.update((p) => p + 1);
  }

  protected onFrom(event: Event): void {
    this.fromDate.set((event.target as HTMLInputElement).value);
    this.pageIndex.set(0);
  }

  protected onTo(event: Event): void {
    this.toDate.set((event.target as HTMLInputElement).value);
    this.pageIndex.set(0);
  }
}
