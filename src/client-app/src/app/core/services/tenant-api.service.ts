import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  CompanyProfile,
  CreateReportRequest,
  CreateReportResponse,
  CreatedApiKey,
  InviteUserRequest,
  NotificationPreference,
  ReportDownloadResponse,
} from '../models/tenant-models';

/**
 * Tenant-portal command service. Per CLAUDE.md / global-context constraint #5, reactive READS use
 * Angular 20 `httpResource` directly in the components. This service owns ONLY the imperative,
 * user-gesture-driven WRITES (create report, delete report, invite/deactivate user, update
 * settings, etc.).
 *
 * Every call targets a tenant-scoped `/api/v1/` endpoint. The authoritative isolation boundary is
 * the server-side TenantResolutionMiddleware + named EF Core query filters + PBAC; the MSAL
 * interceptor attaches the tenant-user bearer token.
 *
 * `mutationVersion` is a monotonically increasing signal that components include in their
 * `httpResource` request computations so a successful write transparently re-fetches the relevant
 * lists — no event bus, no manual subscription churn (mirrors the host portal's HostApiService).
 */
@Injectable({ providedIn: 'root' })
export class TenantApiService {
  private readonly http = inject(HttpClient);

  private readonly _mutationVersion = signal(0);
  /** Read by components inside their `httpResource` request fns to trigger a refetch after writes. */
  readonly mutationVersion = this._mutationVersion.asReadonly();

  private bump(): void {
    this._mutationVersion.update((n) => n + 1);
  }

  // ---- Reports --------------------------------------------------------------

  /** Enqueues a report; the heavy generation runs on a background worker (compute-heavy work queued). */
  async createReport(request: CreateReportRequest): Promise<CreateReportResponse> {
    const res = await firstValueFrom(
      this.http.post<CreateReportResponse>('/api/v1/reports', request),
    );
    this.bump();
    return res;
  }

  /** Returns a short-lived SAS URL for the completed report blob (never a permanent public link). */
  getReportDownloadUrl(reportId: string): Promise<ReportDownloadResponse> {
    return firstValueFrom(
      this.http.get<ReportDownloadResponse>(
        `/api/v1/reports/${encodeURIComponent(reportId)}/download`,
      ),
    );
  }

  /** Soft-deletes a report (server-side soft-delete named query filter hides it thereafter). */
  async deleteReport(reportId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/v1/reports/${encodeURIComponent(reportId)}`));
    this.bump();
  }

  // ---- Team management ------------------------------------------------------

  /** Invites a user; the invitation email is sent by a background job (never inline). */
  async inviteUser(request: InviteUserRequest): Promise<void> {
    await firstValueFrom(this.http.post('/api/v1/team/invitations', request));
    this.bump();
  }

  async updatePermissions(userId: string, permissions: readonly string[]): Promise<void> {
    await firstValueFrom(
      this.http.put(`/api/v1/team/${encodeURIComponent(userId)}/permissions`, { permissions }),
    );
    this.bump();
  }

  async deactivateUser(userId: string): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/v1/team/${encodeURIComponent(userId)}/deactivate`, {}),
    );
    this.bump();
  }

  async reactivateUser(userId: string): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/v1/team/${encodeURIComponent(userId)}/reactivate`, {}),
    );
    this.bump();
  }

  // ---- Settings -------------------------------------------------------------

  async updateCompanyProfile(profile: CompanyProfile): Promise<void> {
    await firstValueFrom(this.http.put('/api/v1/settings/profile', profile));
    this.bump();
  }

  async updateNotificationPreferences(prefs: readonly NotificationPreference[]): Promise<void> {
    await firstValueFrom(this.http.put('/api/v1/settings/notifications', { preferences: prefs }));
    this.bump();
  }

  /** Generates an API key; the plaintext secret is returned ONCE and never persisted client-side. */
  async createApiKey(name: string): Promise<CreatedApiKey> {
    const res = await firstValueFrom(
      this.http.post<CreatedApiKey>('/api/v1/settings/api-keys', { name }),
    );
    this.bump();
    return res;
  }

  async revokeApiKey(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/v1/settings/api-keys/${encodeURIComponent(id)}`));
    this.bump();
  }

  // ---- Danger zone ----------------------------------------------------------

  /** Enqueues a full-data export (background job); returns the tracking job id. */
  requestDataExport(): Promise<{ jobId: string }> {
    return firstValueFrom(this.http.post<{ jobId: string }>('/api/v1/settings/export', {}));
  }

  /** Requests account deletion (server starts the soft-delete + retention workflow). */
  async requestAccountDeletion(confirmation: string): Promise<void> {
    await firstValueFrom(
      this.http.post('/api/v1/settings/account-deletion', { confirmation }),
    );
    this.bump();
  }
}
