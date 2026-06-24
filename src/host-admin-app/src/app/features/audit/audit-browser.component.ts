import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { AuditLogEntry, PagedResult } from '../../core/models/host-models';
import { HostApiService } from '../../core/services/host-api.service';

/**
 * Global cross-tenant audit browser (host operators only). Filter signals drive a paged
 * `httpResource` read; export is delegated to a background job (heavy work is queued, not inline).
 *
 * `tenantId` may be pre-seeded from a query param (e.g. when arriving from a tenant detail page)
 * via component input binding. Endpoints (HostOperatorsOnly):
 *   GET  /api/v1/host/audit
 *   POST /api/v1/host/audit/export
 */
@Component({
  selector: 'po-audit-browser',
  standalone: true,
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section aria-labelledby="audit-heading" class="space-y-4">
      <div class="flex items-center justify-between">
        <h2 id="audit-heading" class="text-lg font-semibold">Global Audit Browser</h2>
        <button
          type="button"
          (click)="exportToExcel()"
          [disabled]="exporting()"
          class="rounded-md border border-slate-300 px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
        >
          {{ exporting() ? 'Queuing export…' : 'Export to Excel' }}
        </button>
      </div>

      @if (exportMessage()) {
        <p class="rounded-md bg-emerald-50 px-3 py-2 text-sm text-emerald-700" role="status">
          {{ exportMessage() }}
        </p>
      }

      <!-- Filters -->
      <fieldset class="grid grid-cols-1 gap-3 rounded-lg border border-slate-200 bg-white p-4 md:grid-cols-3 lg:grid-cols-6">
        <legend class="sr-only">Audit filters</legend>
        <div>
          <label for="f-tenant" class="block text-xs font-medium text-slate-500">Tenant ID</label>
          <input id="f-tenant" type="text" [value]="tenantIdFilter()" (input)="set('tenant', $any($event.target).value)"
            class="mt-1 w-full rounded-md border border-slate-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none" />
        </div>
        <div>
          <label for="f-user" class="block text-xs font-medium text-slate-500">User ID</label>
          <input id="f-user" type="text" [value]="userId()" (input)="set('user', $any($event.target).value)"
            class="mt-1 w-full rounded-md border border-slate-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none" />
        </div>
        <div>
          <label for="f-action" class="block text-xs font-medium text-slate-500">Action</label>
          <input id="f-action" type="text" [value]="action()" (input)="set('action', $any($event.target).value)"
            class="mt-1 w-full rounded-md border border-slate-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none" />
        </div>
        <div>
          <label for="f-table" class="block text-xs font-medium text-slate-500">Table</label>
          <input id="f-table" type="text" [value]="tableName()" (input)="set('table', $any($event.target).value)"
            class="mt-1 w-full rounded-md border border-slate-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none" />
        </div>
        <div>
          <label for="f-from" class="block text-xs font-medium text-slate-500">From</label>
          <input id="f-from" type="date" [value]="fromDate()" (input)="set('from', $any($event.target).value)"
            class="mt-1 w-full rounded-md border border-slate-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none" />
        </div>
        <div>
          <label for="f-to" class="block text-xs font-medium text-slate-500">To</label>
          <input id="f-to" type="date" [value]="toDate()" (input)="set('to', $any($event.target).value)"
            class="mt-1 w-full rounded-md border border-slate-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none" />
        </div>
      </fieldset>

      <div class="overflow-hidden rounded-lg border border-slate-200 bg-white">
        @if (logs.isLoading()) {
          <p class="p-6 text-sm text-slate-500" role="status">Loading audit log…</p>
        } @else if (logs.error()) {
          <p class="p-6 text-sm text-red-600" role="alert">Failed to load audit log.</p>
        } @else {
          <table role="grid" class="min-w-full divide-y divide-slate-200 text-sm">
            <caption class="sr-only">Cross-tenant audit log entries</caption>
            <thead class="bg-slate-50">
              <tr>
                <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Timestamp</th>
                <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Tenant</th>
                <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">User</th>
                <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Action</th>
                <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Table</th>
                <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Summary</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-100">
              @for (a of logs.value()?.items ?? []; track a.id) {
                <tr class="hover:bg-slate-50">
                  <th scope="row" class="px-4 py-2 text-left font-normal text-slate-500">{{ a.timestampUtc | date: 'short' }}</th>
                  <td class="px-4 py-2 font-mono text-xs">{{ a.tenantId }}</td>
                  <td class="px-4 py-2 font-mono text-xs">{{ a.userId }}</td>
                  <td class="px-4 py-2">{{ a.action }}</td>
                  <td class="px-4 py-2 font-mono text-xs">{{ a.tableName }}</td>
                  <td class="px-4 py-2 text-slate-600">{{ a.summary }}</td>
                </tr>
              } @empty {
                <tr><td colspan="6" class="px-4 py-6 text-center text-slate-500">No audit entries match the filters.</td></tr>
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
export class AuditBrowserComponent {
  private readonly api = inject(HostApiService);

  /** Optional pre-seed from ?tenantId= query param (component input binding). */
  readonly tenantId = input<string>('');

  protected readonly tenantIdFilter = signal('');
  protected readonly userId = signal('');
  protected readonly action = signal('');
  protected readonly tableName = signal('');
  protected readonly fromDate = signal('');
  protected readonly toDate = signal('');
  protected readonly page = signal(1);
  protected readonly pageSize = 25;

  protected readonly exporting = signal(false);
  protected readonly exportMessage = signal<string | null>(null);

  constructor() {
    // Seed the tenant filter from the route query param once, if present.
    const seeded = this.tenantId();
    if (seeded) {
      this.tenantIdFilter.set(seeded);
    }
  }

  protected readonly logs = httpResource<PagedResult<AuditLogEntry>>(() => ({
    url: '/api/v1/host/audit',
    params: {
      pageNumber: this.page(),
      pageSize: this.pageSize,
      tenantId: this.tenantIdFilter(),
      userId: this.userId(),
      action: this.action(),
      tableName: this.tableName(),
      from: this.fromDate(),
      to: this.toDate(),
    },
  }));

  protected readonly hasNext = computed(() => {
    const r = this.logs.value();
    return r ? r.pageNumber * this.pageSize < r.totalCount : false;
  });

  protected readonly rangeLabel = computed(() => {
    const r = this.logs.value();
    if (!r || r.totalCount === 0) return 'No results';
    const start = (r.pageNumber - 1) * this.pageSize + 1;
    const end = Math.min(r.pageNumber * this.pageSize, r.totalCount);
    return `${start}–${end} of ${r.totalCount}`;
  });

  protected set(field: 'tenant' | 'user' | 'action' | 'table' | 'from' | 'to', value: string): void {
    switch (field) {
      case 'tenant':
        this.tenantIdFilter.set(value);
        break;
      case 'user':
        this.userId.set(value);
        break;
      case 'action':
        this.action.set(value);
        break;
      case 'table':
        this.tableName.set(value);
        break;
      case 'from':
        this.fromDate.set(value);
        break;
      case 'to':
        this.toDate.set(value);
        break;
    }
    this.page.set(1);
  }

  protected prev(): void {
    this.page.update((p) => Math.max(1, p - 1));
  }

  protected next(): void {
    if (this.hasNext()) this.page.update((p) => p + 1);
  }

  protected async exportToExcel(): Promise<void> {
    this.exporting.set(true);
    this.exportMessage.set(null);
    try {
      const { jobId } = await this.api.requestAuditExport({
        tenantId: this.tenantIdFilter(),
        userId: this.userId(),
        action: this.action(),
        tableName: this.tableName(),
        from: this.fromDate(),
        to: this.toDate(),
      });
      this.exportMessage.set(
        `Export queued (job ${jobId}). You will receive the file when the background job completes.`,
      );
    } catch {
      this.exportMessage.set(null);
    } finally {
      this.exporting.set(false);
    }
  }
}
