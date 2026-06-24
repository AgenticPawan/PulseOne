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
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { PagedResult, ReportSummary } from '../../core/models/tenant-models';
import { TenantApiService } from '../../core/services/tenant-api.service';
import { ReportHubService } from '../../core/services/report-hub.service';
import { StatusBadgeComponent } from '../../shared/status-badge.component';
import { FileSizePipe } from '../../shared/file-size.pipe';
import { ReportCreateComponent } from './report-create.component';

/**
 * Reports grid — the canonical blueprint §6.5 `httpResource` reference, extended to the full prompt
 * 01 spec (search, sort headers, pagination, View/Download/Delete row actions, create dialog, and
 * SignalR-driven live completion).
 *
 * Reactive read: the `httpResource` request fn depends on the page/search/sort signals, on
 * `TenantApiService.mutationVersion` (so create/delete refetch), AND on
 * `ReportHubService.completionTick` (so a `ReportCompleted` push refetches the grid). httpResource
 * cancels the prior in-flight request on every change — no `effect()`+subscribe anti-pattern.
 *
 * SignalR: the create flow emits a reportId; we surface an `aria-live` toast when the hub reports
 * that report completed, then auto-refresh via the completion tick.
 */
@Component({
  selector: 'po-report-grid',
  standalone: true,
  imports: [DatePipe, StatusBadgeComponent, FileSizePipe, ReportCreateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section aria-labelledby="reports-heading" class="space-y-4">
      <div class="flex items-center justify-between">
        <h2 id="reports-heading" class="text-lg font-semibold text-slate-900">Reports</h2>
        <button
          type="button"
          (click)="openCreate()"
          class="rounded-md bg-[var(--tenant-primary,#2563eb)] px-3 py-2 text-sm font-medium text-white hover:opacity-90"
        >
          Generate report
        </button>
      </div>

      <!-- Live region: announces report completion pushed over SignalR. -->
      <div aria-live="polite" class="sr-only">{{ liveMessage() }}</div>
      @if (toast(); as t) {
        <div
          role="status"
          class="rounded-md border border-emerald-200 bg-emerald-50 px-4 py-2 text-sm text-emerald-800"
        >
          {{ t }}
        </div>
      }

      <div class="flex flex-wrap items-center gap-3">
        <label class="sr-only" for="report-search">Search reports</label>
        <input
          id="report-search"
          type="search"
          placeholder="Search by name"
          [value]="searchFilter()"
          (input)="onSearch($any($event.target).value)"
          class="w-64 rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
        />
      </div>

      <div class="overflow-hidden rounded-lg border border-slate-200 bg-white">
        <div aria-live="polite">
          @if (reports.isLoading()) {
            <p class="p-6 text-sm text-slate-500" role="status">Loading reports…</p>
          } @else if (reports.error()) {
            <p class="p-6 text-sm text-red-600" role="alert">Failed to load reports.</p>
          } @else {
            <table role="grid" class="min-w-full divide-y divide-slate-200 text-sm">
              <caption class="sr-only">Your generated reports</caption>
              <thead class="bg-slate-50">
                <tr>
                  @for (col of columns; track col.key) {
                    <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">
                      @if (col.sortable) {
                        <button
                          type="button"
                          class="inline-flex items-center gap-1 hover:text-slate-700"
                          (click)="onSort(col.key)"
                          [attr.aria-label]="'Sort by ' + col.label"
                        >
                          {{ col.label }}
                          @if (sortColumn() === col.key) {
                            <span aria-hidden="true">{{ sortDirection() === 'asc' ? '▲' : '▼' }}</span>
                          }
                        </button>
                      } @else {
                        {{ col.label }}
                      }
                    </th>
                  }
                </tr>
              </thead>
              <tbody class="divide-y divide-slate-100">
                @for (r of reports.value()?.items ?? []; track r.id) {
                  <tr class="hover:bg-slate-50">
                    <th scope="row" class="px-4 py-2 text-left font-medium text-slate-800">
                      {{ r.reportName }}
                    </th>
                    <td class="px-4 py-2 text-slate-600">{{ r.reportType }}</td>
                    <td class="px-4 py-2"><po-status-badge [status]="r.status" /></td>
                    <td class="px-4 py-2 text-slate-500">{{ r.sizeBytes | fileSize }}</td>
                    <td class="px-4 py-2 text-slate-500">{{ r.createdAtUtc | date: 'short' }}</td>
                    <td class="px-4 py-2">
                      <div class="flex items-center gap-3">
                        <button
                          type="button"
                          (click)="download(r)"
                          [disabled]="r.status !== 'Completed' || busy() === r.id"
                          class="text-indigo-600 hover:underline disabled:opacity-40"
                          [attr.aria-label]="'Download ' + r.reportName"
                        >
                          Download
                        </button>
                        <button
                          type="button"
                          (click)="remove(r)"
                          [disabled]="busy() === r.id"
                          class="text-red-600 hover:underline disabled:opacity-40"
                          [attr.aria-label]="'Delete ' + r.reportName"
                        >
                          Delete
                        </button>
                      </div>
                    </td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="6" class="px-4 py-6 text-center text-slate-500">No reports yet.</td>
                  </tr>
                }
              </tbody>
            </table>

            <div
              class="flex items-center justify-between border-t border-slate-200 px-4 py-3 text-sm"
            >
              <span class="text-slate-500">{{ rangeLabel() }}</span>
              <div class="flex gap-2">
                <button
                  type="button"
                  (click)="prevPage()"
                  [disabled]="pageIndex() <= 1"
                  class="rounded-md border border-slate-300 px-3 py-1 disabled:opacity-50"
                  aria-label="Previous page"
                >
                  Previous
                </button>
                <button
                  type="button"
                  (click)="nextPage()"
                  [disabled]="!hasNextPage()"
                  class="rounded-md border border-slate-300 px-3 py-1 disabled:opacity-50"
                  aria-label="Next page"
                >
                  Next
                </button>
              </div>
            </div>
          }
        </div>
      </div>
    </section>

    @if (showCreate()) {
      <po-report-create (created)="onCreated($event)" (cancelled)="showCreate.set(false)" />
    }
  `,
})
export class ReportGridComponent {
  private readonly api = inject(TenantApiService);
  private readonly hub = inject(ReportHubService);

  protected readonly columns = [
    { key: 'reportName', label: 'Name', sortable: true },
    { key: 'reportType', label: 'Type', sortable: true },
    { key: 'status', label: 'Status', sortable: true },
    { key: 'sizeBytes', label: 'Size', sortable: false },
    { key: 'createdAtUtc', label: 'Created', sortable: true },
    { key: 'actions', label: 'Actions', sortable: false },
  ] as const;

  // Canonical blueprint §6.5 signals.
  protected readonly pageIndex = signal(1);
  protected readonly searchFilter = signal('');
  protected readonly sortColumn = signal('createdAtUtc');
  protected readonly sortDirection = signal<'asc' | 'desc'>('desc');
  protected readonly pageSize = 10;

  protected readonly busy = signal<string | null>(null);
  protected readonly showCreate = signal(false);
  protected readonly toast = signal<string | null>(null);
  protected readonly liveMessage = signal('');

  /** Ids of reports created this session, so we only toast on OUR completions. */
  private readonly trackedReportIds = new Set<string>();

  constructor() {
    // Relay hub completion events to the live region/toast. The grid refetch itself is driven
    // reactively by the completionTick signal inside the httpResource request fn below.
    this.hub.reportCompleted$.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe((evt) => {
      if (this.trackedReportIds.has(evt.reportId)) {
        this.toast.set('Your report is ready to download.');
        this.liveMessage.set(`Report ${evt.reportId} completed and is ready to download.`);
      }
    });
  }

  // Reactive read (blueprint §6.5): recomputes on signal change AND cancels the previous request.
  protected readonly reports = httpResource<PagedResult<ReportSummary>>(() => {
    // Reading these makes writes (mutationVersion) and SignalR completions (completionTick) refetch.
    this.api.mutationVersion();
    this.hub.completionTick();
    return {
      url: '/api/v1/reports',
      params: {
        pageNumber: this.pageIndex(),
        pageSize: this.pageSize,
        searchTerm: this.searchFilter(),
        sortColumn: this.sortColumn(),
        sortOrder: this.sortDirection(),
      },
    };
  });

  protected readonly hasNextPage = computed(() => {
    const r = this.reports.value();
    return r ? r.pageNumber * this.pageSize < r.totalCount : false;
  });

  protected readonly rangeLabel = computed(() => {
    const r = this.reports.value();
    if (!r || r.totalCount === 0) {
      return 'No results';
    }
    const start = (r.pageNumber - 1) * this.pageSize + 1;
    const end = Math.min(r.pageNumber * this.pageSize, r.totalCount);
    return `${start}–${end} of ${r.totalCount}`;
  });

  protected onSearch(value: string): void {
    this.searchFilter.set(value);
    this.pageIndex.set(1);
  }

  protected onSort(column: string): void {
    this.sortDirection.set(
      this.sortColumn() === column && this.sortDirection() === 'asc' ? 'desc' : 'asc',
    );
    this.sortColumn.set(column);
  }

  protected prevPage(): void {
    this.pageIndex.update((p) => Math.max(1, p - 1));
  }

  protected nextPage(): void {
    if (this.hasNextPage()) {
      this.pageIndex.update((p) => p + 1);
    }
  }

  protected openCreate(): void {
    this.showCreate.set(true);
  }

  protected onCreated(reportId: string): void {
    this.trackedReportIds.add(reportId);
    this.showCreate.set(false);
    this.toast.set('Report queued. You will be notified when it is ready.');
    this.liveMessage.set('Report queued for generation.');
  }

  protected async download(report: ReportSummary): Promise<void> {
    this.busy.set(report.id);
    try {
      const res = await this.api.getReportDownloadUrl(report.id);
      // SAS URL: open in a new tab; rel handled by the browser default for programmatic open.
      window.open(res.downloadUrl, '_blank', 'noopener');
    } finally {
      this.busy.set(null);
    }
  }

  protected async remove(report: ReportSummary): Promise<void> {
    this.busy.set(report.id);
    try {
      await this.api.deleteReport(report.id);
    } finally {
      this.busy.set(null);
    }
  }
}
