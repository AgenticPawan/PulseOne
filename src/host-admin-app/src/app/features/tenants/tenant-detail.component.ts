import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import {
  AuditLogEntry,
  PagedResult,
  TenantDetail,
  TenantStorageUsage,
  TenantSubscriptionHistoryEntry,
  TenantUserSummary,
} from '../../core/models/host-models';
import { HostApiService } from '../../core/services/host-api.service';
import { StatusBadgeComponent } from '../../shared/status-badge.component';

/**
 * Tenant detail view. The `tenantId` route param is bound via component input binding (provided by
 * `withComponentInputBinding()`). Each section is an INDEPENDENT `httpResource` with its own
 * loading/error state, per the prompt — overview, subscription history, audit logs, users, storage.
 *
 * Endpoints (all HostOperatorsOnly):
 *   GET /api/v1/host/tenants/{id}
 *   GET /api/v1/host/tenants/{id}/subscriptions
 *   GET /api/v1/host/tenants/{id}/audit
 *   GET /api/v1/host/tenants/{id}/users
 *   GET /api/v1/host/tenants/{id}/storage
 */
@Component({
  selector: 'po-tenant-detail',
  standalone: true,
  imports: [RouterLink, DatePipe, DecimalPipe, StatusBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="space-y-6">
      <nav class="text-sm" aria-label="Breadcrumb">
        <a routerLink="/tenants" class="text-indigo-600 hover:underline">Tenants</a>
        <span class="px-1 text-slate-400" aria-hidden="true">/</span>
        <span class="text-slate-600">{{ tenantId() }}</span>
      </nav>

      <!-- Overview -->
      <section aria-labelledby="overview-heading" class="rounded-lg border border-slate-200 bg-white p-5">
        <div class="flex items-center justify-between">
          <h2 id="overview-heading" class="text-lg font-semibold">Overview</h2>
          @if (overview.value(); as o) {
            <div class="flex items-center gap-3">
              <po-status-badge [status]="o.status" />
              @if (o.status === 'Active') {
                <button
                  type="button"
                  (click)="suspend()"
                  [disabled]="busy()"
                  class="rounded-md border border-amber-300 px-3 py-1 text-sm font-medium text-amber-700 hover:bg-amber-50 disabled:opacity-50"
                >
                  Suspend
                </button>
              } @else if (o.status === 'Suspended') {
                <button
                  type="button"
                  (click)="reactivate()"
                  [disabled]="busy()"
                  class="rounded-md border border-emerald-300 px-3 py-1 text-sm font-medium text-emerald-700 hover:bg-emerald-50 disabled:opacity-50"
                >
                  Reactivate
                </button>
              }
            </div>
          }
        </div>
        @if (overview.isLoading()) {
          <p class="mt-3 text-sm text-slate-500" role="status">Loading overview…</p>
        } @else if (overview.error()) {
          <p class="mt-3 text-sm text-red-600" role="alert">Failed to load tenant overview.</p>
        } @else if (overview.value(); as o) {
          <dl class="mt-4 grid grid-cols-2 gap-4 text-sm md:grid-cols-4">
            <div><dt class="text-slate-500">Name</dt><dd class="font-medium">{{ o.name }}</dd></div>
            <div><dt class="text-slate-500">Plan</dt><dd class="font-medium">{{ o.plan }}</dd></div>
            <div><dt class="text-slate-500">Shard</dt><dd class="font-mono text-xs">{{ o.shard }}</dd></div>
            <div><dt class="text-slate-500">Region</dt><dd class="font-medium">{{ o.region }}</dd></div>
            <div><dt class="text-slate-500">Admin email</dt><dd class="font-medium">{{ o.adminEmail }}</dd></div>
            <div><dt class="text-slate-500">Created</dt><dd>{{ o.createdAtUtc | date: 'medium' }}</dd></div>
          </dl>
        }
      </section>

      <!-- Storage usage -->
      <section aria-labelledby="storage-heading" class="rounded-lg border border-slate-200 bg-white p-5">
        <h2 id="storage-heading" class="text-lg font-semibold">Storage usage</h2>
        @if (storage.isLoading()) {
          <p class="mt-3 text-sm text-slate-500" role="status">Loading storage…</p>
        } @else if (storage.error()) {
          <p class="mt-3 text-sm text-red-600" role="alert">Failed to load storage usage.</p>
        } @else if (storage.value(); as s) {
          <div class="mt-4 space-y-2 text-sm">
            <div class="flex justify-between">
              <span class="text-slate-600">{{ mb(s.usedBytes) }} MB of {{ mb(s.quotaBytes) }} MB</span>
              <span class="text-slate-500">{{ s.documentCount | number }} documents</span>
            </div>
            <div class="h-2 w-full overflow-hidden rounded-full bg-slate-100" role="progressbar"
                 [attr.aria-valuenow]="pct(s)" aria-valuemin="0" aria-valuemax="100"
                 [attr.aria-label]="'Storage used: ' + pct(s) + ' percent'">
              <div class="h-full rounded-full bg-indigo-500" [style.width.%]="pct(s)"></div>
            </div>
          </div>
        }
      </section>

      <!-- Subscription history -->
      <section aria-labelledby="subs-heading" class="rounded-lg border border-slate-200 bg-white p-5">
        <h2 id="subs-heading" class="text-lg font-semibold">Subscription history</h2>
        @if (subscriptions.isLoading()) {
          <p class="mt-3 text-sm text-slate-500" role="status">Loading subscriptions…</p>
        } @else if (subscriptions.error()) {
          <p class="mt-3 text-sm text-red-600" role="alert">Failed to load subscription history.</p>
        } @else {
          <table role="grid" class="mt-4 min-w-full divide-y divide-slate-200 text-sm">
            <caption class="sr-only">Subscription history for this tenant</caption>
            <thead class="bg-slate-50">
              <tr>
                <th scope="col" class="px-3 py-2 text-left font-medium text-slate-500">Subscription ID</th>
                <th scope="col" class="px-3 py-2 text-left font-medium text-slate-500">Plan</th>
                <th scope="col" class="px-3 py-2 text-left font-medium text-slate-500">Status</th>
                <th scope="col" class="px-3 py-2 text-left font-medium text-slate-500">Started</th>
                <th scope="col" class="px-3 py-2 text-left font-medium text-slate-500">Ended</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-100">
              @for (s of subscriptions.value() ?? []; track s.subscriptionId) {
                <tr>
                  <th scope="row" class="px-3 py-2 text-left font-mono text-xs">{{ s.subscriptionId }}</th>
                  <td class="px-3 py-2">{{ s.plan }}</td>
                  <td class="px-3 py-2"><po-status-badge [status]="s.status" /></td>
                  <td class="px-3 py-2 text-slate-500">{{ s.startedUtc | date: 'mediumDate' }}</td>
                  <td class="px-3 py-2 text-slate-500">{{ s.endedUtc ? (s.endedUtc | date: 'mediumDate') : '—' }}</td>
                </tr>
              } @empty {
                <tr><td colspan="5" class="px-3 py-4 text-center text-slate-500">No subscription history.</td></tr>
              }
            </tbody>
          </table>
        }
      </section>

      <!-- Users -->
      <section aria-labelledby="users-heading" class="rounded-lg border border-slate-200 bg-white p-5">
        <h2 id="users-heading" class="text-lg font-semibold">Users</h2>
        @if (users.isLoading()) {
          <p class="mt-3 text-sm text-slate-500" role="status">Loading users…</p>
        } @else if (users.error()) {
          <p class="mt-3 text-sm text-red-600" role="alert">Failed to load users.</p>
        } @else {
          <table role="grid" class="mt-4 min-w-full divide-y divide-slate-200 text-sm">
            <caption class="sr-only">Users belonging to this tenant</caption>
            <thead class="bg-slate-50">
              <tr>
                <th scope="col" class="px-3 py-2 text-left font-medium text-slate-500">Email</th>
                <th scope="col" class="px-3 py-2 text-left font-medium text-slate-500">Role</th>
                <th scope="col" class="px-3 py-2 text-left font-medium text-slate-500">Last login</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-100">
              @for (u of users.value() ?? []; track u.userId) {
                <tr>
                  <th scope="row" class="px-3 py-2 text-left font-normal">{{ u.email }}</th>
                  <td class="px-3 py-2">{{ u.role }}</td>
                  <td class="px-3 py-2 text-slate-500">{{ u.lastLoginUtc ? (u.lastLoginUtc | date: 'short') : 'Never' }}</td>
                </tr>
              } @empty {
                <tr><td colspan="3" class="px-3 py-4 text-center text-slate-500">No users.</td></tr>
              }
            </tbody>
          </table>
        }
      </section>

      <!-- Audit logs -->
      <section aria-labelledby="tenant-audit-heading" class="rounded-lg border border-slate-200 bg-white p-5">
        <div class="flex items-center justify-between">
          <h2 id="tenant-audit-heading" class="text-lg font-semibold">Recent audit activity</h2>
          <a [routerLink]="['/audit']" [queryParams]="{ tenantId: tenantId() }" class="text-sm text-indigo-600 hover:underline">
            Open in audit browser
          </a>
        </div>
        @if (audit.isLoading()) {
          <p class="mt-3 text-sm text-slate-500" role="status">Loading audit logs…</p>
        } @else if (audit.error()) {
          <p class="mt-3 text-sm text-red-600" role="alert">Failed to load audit logs.</p>
        } @else {
          <table role="grid" class="mt-4 min-w-full divide-y divide-slate-200 text-sm">
            <caption class="sr-only">Recent audit log entries for this tenant</caption>
            <thead class="bg-slate-50">
              <tr>
                <th scope="col" class="px-3 py-2 text-left font-medium text-slate-500">When</th>
                <th scope="col" class="px-3 py-2 text-left font-medium text-slate-500">User</th>
                <th scope="col" class="px-3 py-2 text-left font-medium text-slate-500">Action</th>
                <th scope="col" class="px-3 py-2 text-left font-medium text-slate-500">Table</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-100">
              @for (a of audit.value()?.items ?? []; track a.id) {
                <tr>
                  <th scope="row" class="px-3 py-2 text-left font-normal text-slate-500">{{ a.timestampUtc | date: 'short' }}</th>
                  <td class="px-3 py-2 font-mono text-xs">{{ a.userId }}</td>
                  <td class="px-3 py-2">{{ a.action }}</td>
                  <td class="px-3 py-2 font-mono text-xs">{{ a.tableName }}</td>
                </tr>
              } @empty {
                <tr><td colspan="4" class="px-3 py-4 text-center text-slate-500">No recent activity.</td></tr>
              }
            </tbody>
          </table>
        }
      </section>
    </div>
  `,
})
export class TenantDetailComponent {
  private readonly api = inject(HostApiService);

  /** Bound from the :tenantId route param via withComponentInputBinding(). */
  readonly tenantId = input.required<string>();

  protected readonly busy = signal(false);

  private base() {
    return `/api/v1/host/tenants/${encodeURIComponent(this.tenantId())}`;
  }

  protected readonly overview = httpResource<TenantDetail>(() => {
    this.api.mutationVersion();
    return { url: this.base() };
  });

  protected readonly storage = httpResource<TenantStorageUsage>(() => ({
    url: `${this.base()}/storage`,
  }));

  protected readonly subscriptions = httpResource<readonly TenantSubscriptionHistoryEntry[]>(() => {
    this.api.mutationVersion();
    return { url: `${this.base()}/subscriptions` };
  });

  protected readonly users = httpResource<readonly TenantUserSummary[]>(() => ({
    url: `${this.base()}/users`,
  }));

  protected readonly audit = httpResource<PagedResult<AuditLogEntry>>(() => ({
    url: `${this.base()}/audit`,
    params: { pageNumber: 1, pageSize: 10 },
  }));

  protected mb(bytes: number): string {
    return (bytes / (1024 * 1024)).toFixed(1);
  }

  protected pct(s: TenantStorageUsage): number {
    if (!s.quotaBytes) return 0;
    return Math.min(100, Math.round((s.usedBytes / s.quotaBytes) * 100));
  }

  protected async suspend(): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.suspendTenant(this.tenantId());
    } finally {
      this.busy.set(false);
    }
  }

  protected async reactivate(): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.reactivateTenant(this.tenantId());
    } finally {
      this.busy.set(false);
    }
  }
}
