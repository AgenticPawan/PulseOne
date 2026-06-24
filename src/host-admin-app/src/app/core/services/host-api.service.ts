import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ProvisionTenantRequest } from '../models/host-models';

/**
 * Host-portal command service. Per CLAUDE.md / global-context constraint #5, reactive READS use
 * Angular 20 `httpResource` directly in the components. This service owns ONLY the imperative,
 * user-gesture-driven WRITES (provision/suspend/reactivate, subscription overrides, audit export).
 *
 * Every call targets a `/api/v1/host/` endpoint guarded by the server-side `HostOperatorsOnly`
 * policy (CLAUDE.md security rule #4). The MSAL interceptor attaches the operator bearer token.
 *
 * `mutationVersion` is a monotonically increasing signal that components include in their
 * `httpResource` request computations so a successful write transparently re-fetches the relevant
 * lists — no event bus, no manual subscription churn.
 */
@Injectable({ providedIn: 'root' })
export class HostApiService {
  private readonly http = inject(HttpClient);

  private readonly _mutationVersion = signal(0);
  /** Read by components inside their `httpResource` request fns to trigger a refetch after writes. */
  readonly mutationVersion = this._mutationVersion.asReadonly();

  private bump(): void {
    this._mutationVersion.update((n) => n + 1);
  }

  // ---- Tenant lifecycle -----------------------------------------------------

  /** Transactionally provisions a tenant (catalog shard entry + welcome-email background job). */
  async provisionTenant(request: ProvisionTenantRequest): Promise<void> {
    await firstValueFrom(this.http.post('/api/v1/host/tenants', request));
    this.bump();
  }

  async suspendTenant(tenantId: string): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/v1/host/tenants/${encodeURIComponent(tenantId)}/suspend`, {}),
    );
    this.bump();
  }

  async reactivateTenant(tenantId: string): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/v1/host/tenants/${encodeURIComponent(tenantId)}/reactivate`, {}),
    );
    this.bump();
  }

  // ---- Subscription manual overrides ---------------------------------------

  async extendTrial(subscriptionId: string, days: number): Promise<void> {
    await firstValueFrom(
      this.http.post(
        `/api/v1/host/subscriptions/${encodeURIComponent(subscriptionId)}/extend-trial`,
        { days },
      ),
    );
    this.bump();
  }

  async applyDiscount(subscriptionId: string, percent: number): Promise<void> {
    await firstValueFrom(
      this.http.post(
        `/api/v1/host/subscriptions/${encodeURIComponent(subscriptionId)}/discount`,
        { percent },
      ),
    );
    this.bump();
  }

  async cancelSubscription(subscriptionId: string): Promise<void> {
    await firstValueFrom(
      this.http.post(
        `/api/v1/host/subscriptions/${encodeURIComponent(subscriptionId)}/cancel`,
        {},
      ),
    );
    this.bump();
  }

  // ---- Audit export (background job) ---------------------------------------

  /**
   * Enqueues a cross-tenant audit export. The server returns a job id and runs the heavy Excel
   * generation on a background worker (blueprint: compute-heavy work is always queued).
   */
  async requestAuditExport(filters: Record<string, string | undefined>): Promise<{ jobId: string }> {
    const body = Object.fromEntries(
      Object.entries(filters).filter(([, v]) => v != null && v !== ''),
    );
    return firstValueFrom(
      this.http.post<{ jobId: string }>('/api/v1/host/audit/export', body),
    );
  }
}
