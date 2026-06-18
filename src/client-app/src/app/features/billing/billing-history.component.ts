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
    <section class="billing-history">
      <h2 class="billing-history__title">Payment history</h2>

      <div class="billing-history__filters">
        <label>
          From
          <input type="date" [value]="fromDate()" (change)="onFrom($event)" />
        </label>
        <label>
          To
          <input type="date" [value]="toDate()" (change)="onTo($event)" />
        </label>
      </div>

      @if (history.isLoading()) {
        <p>Loading…</p>
      } @else if (history.error()) {
        <p class="billing-history__error">Could not load payment history.</p>
      } @else {
        <table class="billing-history__table">
          <thead>
            <tr>
              <th>Date</th>
              <th>Amount</th>
              <th>Status</th>
              <th>Invoice</th>
            </tr>
          </thead>
          <tbody>
            @for (row of history.value()?.items ?? []; track row.id) {
              <tr>
                <td>{{ row.date }}</td>
                <td>₹{{ row.amountInRupees }}</td>
                <td>{{ row.status }}</td>
                <td>
                  @if (row.invoiceUrl) {
                    <a [href]="row.invoiceUrl" rel="noopener" target="_blank">Download</a>
                  } @else {
                    <span>—</span>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>

        <div class="billing-history__pager">
          <button type="button" [disabled]="pageIndex() === 0" (click)="prev()">Previous</button>
          <span>Page {{ pageIndex() + 1 }}</span>
          <button type="button" (click)="next()">Next</button>
        </div>
      }
    </section>
  `,
  styleUrl: './billing-history.component.scss',
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
