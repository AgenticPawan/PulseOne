import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { PagedResult, TenantSummary } from '../../core/models/host-models';
import { HostApiService } from '../../core/services/host-api.service';
import { StatusBadgeComponent } from '../../shared/status-badge.component';

/**
 * Tenant management list. Reactive read via `httpResource` (constraint #5): the request fn depends
 * on the search/sort/page/status signals AND on `HostApiService.mutationVersion`, so editing a
 * tenant (suspend/reactivate/provision) transparently refetches. Cancellation of in-flight
 * requests on rapid typing is handled by `httpResource`.
 *
 * Endpoint: GET /api/v1/host/tenants (HostOperatorsOnly).
 */
@Component({
  selector: 'po-tenant-list',
  standalone: true,
  imports: [RouterLink, DatePipe, StatusBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section aria-labelledby="tenants-heading" class="space-y-4">
      <div class="flex items-center justify-between">
        <h2 id="tenants-heading" class="text-lg font-semibold">Tenants</h2>
        <a
          routerLink="/tenants/new"
          class="rounded-md bg-indigo-600 px-3 py-2 text-sm font-medium text-white hover:bg-indigo-500"
        >
          Provision tenant
        </a>
      </div>

      <div class="flex flex-wrap items-center gap-3">
        <label class="sr-only" for="tenant-search">Search tenants</label>
        <input
          id="tenant-search"
          type="search"
          placeholder="Search by name or ID"
          [value]="search()"
          (input)="onSearch($any($event.target).value)"
          class="w-64 rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
        />
        <label class="sr-only" for="tenant-status-filter">Filter by status</label>
        <select
          id="tenant-status-filter"
          [value]="statusFilter()"
          (change)="onStatusFilter($any($event.target).value)"
          class="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none"
        >
          <option value="">All statuses</option>
          <option value="Active">Active</option>
          <option value="Suspended">Suspended</option>
          <option value="Provisioning">Provisioning</option>
          <option value="Decommissioned">Decommissioned</option>
        </select>
      </div>

      <div class="overflow-hidden rounded-lg border border-slate-200 bg-white">
        @if (tenants.isLoading()) {
          <p class="p-6 text-sm text-slate-500" role="status">Loading tenants…</p>
        } @else if (tenants.error()) {
          <p class="p-6 text-sm text-red-600" role="alert">
            Failed to load tenants. The request may have been rejected by the host policy.
          </p>
        } @else {
          <table role="grid" class="min-w-full divide-y divide-slate-200 text-sm">
            <caption class="sr-only">List of platform tenants</caption>
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
                          <span aria-hidden="true">{{ sortDir() === 'asc' ? '▲' : '▼' }}</span>
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
              @for (t of tenants.value()?.items ?? []; track t.tenantId) {
                <tr class="hover:bg-slate-50">
                  <th scope="row" class="px-4 py-2 text-left font-mono text-xs text-slate-700">
                    {{ t.tenantId }}
                  </th>
                  <td class="px-4 py-2">{{ t.name }}</td>
                  <td class="px-4 py-2">{{ t.plan }}</td>
                  <td class="px-4 py-2 font-mono text-xs">{{ t.shard }}</td>
                  <td class="px-4 py-2"><po-status-badge [status]="t.status" /></td>
                  <td class="px-4 py-2 text-slate-500">{{ t.createdAtUtc | date: 'mediumDate' }}</td>
                  <td class="px-4 py-2">
                    <div class="flex items-center gap-2">
                      <a
                        [routerLink]="['/tenants', t.tenantId]"
                        class="text-indigo-600 hover:underline"
                        [attr.aria-label]="'View details for ' + t.name"
                        >View</a
                      >
                      @if (t.status === 'Active') {
                        <button
                          type="button"
                          (click)="suspend(t.tenantId)"
                          [disabled]="busy() === t.tenantId"
                          class="text-amber-600 hover:underline disabled:opacity-50"
                          [attr.aria-label]="'Suspend ' + t.name"
                        >
                          Suspend
                        </button>
                      } @else if (t.status === 'Suspended') {
                        <button
                          type="button"
                          (click)="reactivate(t.tenantId)"
                          [disabled]="busy() === t.tenantId"
                          class="text-emerald-600 hover:underline disabled:opacity-50"
                          [attr.aria-label]="'Reactivate ' + t.name"
                        >
                          Reactivate
                        </button>
                      }
                    </div>
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="7" class="px-4 py-6 text-center text-slate-500">No tenants found.</td>
                </tr>
              }
            </tbody>
          </table>

          <!-- Pager -->
          <div class="flex items-center justify-between border-t border-slate-200 px-4 py-3 text-sm">
            <span class="text-slate-500">{{ rangeLabel() }}</span>
            <div class="flex gap-2">
              <button
                type="button"
                (click)="prevPage()"
                [disabled]="page() <= 1"
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
    </section>
  `,
})
export class TenantListComponent {
  private readonly api = inject(HostApiService);

  protected readonly columns = [
    { key: 'tenantId', label: 'Tenant ID', sortable: true },
    { key: 'name', label: 'Name', sortable: true },
    { key: 'plan', label: 'Plan', sortable: true },
    { key: 'shard', label: 'Shard', sortable: false },
    { key: 'status', label: 'Status', sortable: true },
    { key: 'createdAtUtc', label: 'Created', sortable: true },
    { key: 'actions', label: 'Actions', sortable: false },
  ] as const;

  protected readonly search = signal('');
  protected readonly statusFilter = signal('');
  protected readonly sortColumn = signal('createdAtUtc');
  protected readonly sortDir = signal<'asc' | 'desc'>('desc');
  protected readonly page = signal(1);
  protected readonly pageSize = 20;
  protected readonly busy = signal<string | null>(null);

  protected readonly tenants = httpResource<PagedResult<TenantSummary>>(() => {
    // Reading mutationVersion makes writes refetch this list.
    this.api.mutationVersion();
    return {
      url: '/api/v1/host/tenants',
      params: {
        pageNumber: this.page(),
        pageSize: this.pageSize,
        searchTerm: this.search(),
        status: this.statusFilter(),
        sortColumn: this.sortColumn(),
        sortOrder: this.sortDir(),
      },
    };
  });

  protected readonly hasNextPage = computed(() => {
    const r = this.tenants.value();
    return r ? r.pageNumber * this.pageSize < r.totalCount : false;
  });

  protected readonly rangeLabel = computed(() => {
    const r = this.tenants.value();
    if (!r || r.totalCount === 0) return 'No results';
    const start = (r.pageNumber - 1) * this.pageSize + 1;
    const end = Math.min(r.pageNumber * this.pageSize, r.totalCount);
    return `${start}–${end} of ${r.totalCount}`;
  });

  protected onSearch(value: string): void {
    this.search.set(value);
    this.page.set(1);
  }

  protected onStatusFilter(value: string): void {
    this.statusFilter.set(value);
    this.page.set(1);
  }

  protected onSort(column: string): void {
    if (this.sortColumn() === column) {
      this.sortDir.update((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.sortColumn.set(column);
      this.sortDir.set('asc');
    }
  }

  protected prevPage(): void {
    this.page.update((p) => Math.max(1, p - 1));
  }

  protected nextPage(): void {
    if (this.hasNextPage()) this.page.update((p) => p + 1);
  }

  protected async suspend(tenantId: string): Promise<void> {
    this.busy.set(tenantId);
    try {
      await this.api.suspendTenant(tenantId);
    } finally {
      this.busy.set(null);
    }
  }

  protected async reactivate(tenantId: string): Promise<void> {
    this.busy.set(tenantId);
    try {
      await this.api.reactivateTenant(tenantId);
    } finally {
      this.busy.set(null);
    }
  }
}
