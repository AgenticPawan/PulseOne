/**
 * Shared DTO contracts for the tenant portal (Phase 6). These mirror the server-side response
 * shapes returned by the tenant-scoped `/api/v1/` endpoints. Keeping them in one place avoids
 * duplicating interface definitions across features (CLAUDE.md: no duplicate infrastructure).
 *
 * SECURITY NOTE: every one of these is consumed ONLY through tenant-scoped endpoints. The
 * authoritative isolation boundary is the server-side TenantResolutionMiddleware + named EF Core
 * query filters + PBAC (CLAUDE.md). These types carry no security weight on their own; the router
 * guards and any client-side claim reads are UI convenience only.
 */

/** Generic server paging envelope (matches the backend `PagedResult<T>` contract). */
export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly totalCount: number;
  readonly pageNumber: number;
  readonly pageSize: number;
}

export type SortDirection = 'asc' | 'desc';

// ---- Dashboard --------------------------------------------------------------

/** KPI summary for the dashboard cards. GET /api/v1/dashboard/summary (OWED — see report). */
export interface DashboardSummary {
  readonly activeUsers: number;
  readonly reportsGenerated: number;
  readonly storageUsedBytes: number;
  readonly storageQuotaBytes: number;
  readonly currentPlan: string;
}

/** A single recent-activity feed row. GET /api/v1/dashboard/activity (OWED — see report). */
export interface ActivityEntry {
  readonly id: string;
  readonly action: string;
  readonly summary: string;
  readonly actorEmail: string;
  readonly timestampUtc: string;
}

// ---- Reports ----------------------------------------------------------------

export type ReportStatus = 'Queued' | 'Processing' | 'Completed' | 'Failed';

/** A report row. GET /api/v1/reports (OWED — see report). */
export interface ReportSummary {
  readonly id: string;
  readonly reportName: string;
  readonly reportType: string;
  readonly status: ReportStatus;
  readonly createdAtUtc: string;
  readonly sizeBytes: number | null;
}

/** Available report type descriptor used by the create dialog. */
export interface ReportTypeDescriptor {
  readonly key: string;
  readonly label: string;
  readonly parameters: readonly ReportParameterDescriptor[];
}

export interface ReportParameterDescriptor {
  readonly key: string;
  readonly label: string;
  readonly kind: 'text' | 'date' | 'number' | 'select';
  readonly required: boolean;
  readonly options?: readonly string[];
}

/** Payload for POST /api/v1/reports. */
export interface CreateReportRequest {
  readonly reportType: string;
  readonly parameters: Record<string, string>;
}

/** Response for POST /api/v1/reports — the report id to track over the SignalR hub. */
export interface CreateReportResponse {
  readonly reportId: string;
}

/** Response for GET /api/v1/reports/{id}/download — a short-lived SAS URL. */
export interface ReportDownloadResponse {
  readonly downloadUrl: string;
}

// ---- Team management --------------------------------------------------------

export type TeamMemberStatus = 'Active' | 'Invited' | 'Deactivated';

/** GET /api/v1/team (OWED — see report). */
export interface TeamMember {
  readonly userId: string;
  readonly email: string;
  readonly displayName: string;
  readonly role: string;
  readonly status: TeamMemberStatus;
  readonly lastLoginUtc: string | null;
  readonly permissions: readonly string[];
}

/** A PBAC permission grouped by category, for the edit-permissions checkboxes. */
export interface PermissionDescriptor {
  readonly key: string;
  readonly label: string;
  readonly category: string;
}

/** Payload for POST /api/v1/team/invitations. */
export interface InviteUserRequest {
  readonly email: string;
  readonly role: string;
}

// ---- Settings ---------------------------------------------------------------

/** GET/PUT /api/v1/settings/profile (OWED — see report). */
export interface CompanyProfile {
  readonly companyName: string;
  readonly contactEmail: string;
  readonly contactPhone: string;
  readonly logoUrl: string | null;
}

export type NotificationChannel = 'email' | 'sms' | 'whatsapp';

export interface NotificationPreference {
  readonly eventType: string;
  readonly eventLabel: string;
  readonly email: boolean;
  readonly sms: boolean;
  readonly whatsapp: boolean;
}

/** GET /api/v1/settings/api-keys (OWED — see report). The secret is shown ONCE on creation only. */
export interface ApiKeySummary {
  readonly id: string;
  readonly name: string;
  readonly prefix: string;
  readonly createdAtUtc: string;
  readonly lastUsedUtc: string | null;
}

export interface CreatedApiKey {
  readonly id: string;
  readonly name: string;
  readonly secret: string;
}
