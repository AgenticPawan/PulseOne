/**
 * Shared DTO contracts for the host admin portal. These mirror the server-side response shapes
 * returned by the `HostOperatorsOnly`-protected endpoints under `/api/v1/host/` (blueprint §6,
 * QA §7.3). Keeping them in one place avoids duplicating interface definitions across features.
 *
 * NOTE: every one of these is consumed ONLY through the host API. The authoritative authorization
 * boundary is the server-side `HostOperatorsOnly` policy (CLAUDE.md security rule #4); these types
 * carry no security weight on their own.
 */

/** Generic server paging envelope (matches the backend `PagedResult<T>` contract). */
export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly totalCount: number;
  readonly pageNumber: number;
  readonly pageSize: number;
}

export type TenantStatus = 'Active' | 'Suspended' | 'Provisioning' | 'Decommissioned';

export interface TenantSummary {
  readonly tenantId: string;
  readonly name: string;
  readonly plan: string;
  readonly shard: string;
  readonly status: TenantStatus;
  readonly createdAtUtc: string;
}

export interface TenantUserSummary {
  readonly userId: string;
  readonly email: string;
  readonly role: string;
  readonly lastLoginUtc: string | null;
}

export interface TenantStorageUsage {
  readonly usedBytes: number;
  readonly quotaBytes: number;
  readonly documentCount: number;
}

export interface TenantSubscriptionHistoryEntry {
  readonly subscriptionId: string;
  readonly plan: string;
  readonly status: string;
  readonly startedUtc: string;
  readonly endedUtc: string | null;
}

export interface TenantDetail {
  readonly tenantId: string;
  readonly name: string;
  readonly plan: string;
  readonly shard: string;
  readonly status: TenantStatus;
  readonly createdAtUtc: string;
  readonly adminEmail: string;
  readonly region: string;
}

/** Payload for POST /api/v1/host/tenants. */
export interface ProvisionTenantRequest {
  readonly tenantId: string;
  readonly companyName: string;
  readonly planTier: string;
  readonly assignedShard: string;
  readonly adminEmail: string;
}

export type SubscriptionStatus =
  | 'active'
  | 'trialing'
  | 'past_due'
  | 'cancelled'
  | 'pending_cancellation';

export interface SubscriptionSummary {
  readonly tenantId: string;
  readonly tenantName: string;
  readonly razorpaySubscriptionId: string;
  readonly plan: string;
  readonly status: SubscriptionStatus;
  readonly nextBillingUtc: string | null;
  readonly amountInPaise: number;
}

export interface SubscriptionMetrics {
  readonly activeSubscriptions: number;
  readonly monthlyRecurringRevenueInPaise: number;
  readonly churnRatePercent: number;
  readonly pendingCancellations: number;
}

export interface AuditLogEntry {
  readonly id: string;
  readonly tenantId: string;
  readonly userId: string;
  readonly action: string;
  readonly tableName: string;
  readonly timestampUtc: string;
  readonly summary: string;
}

export type HealthState = 'Healthy' | 'Degraded' | 'Unhealthy';

export interface HealthCheckEntry {
  readonly name: string;
  readonly status: HealthState;
  readonly description: string | null;
  readonly durationMs: number;
}

export interface HealthReport {
  readonly status: HealthState;
  readonly checks: readonly HealthCheckEntry[];
}

export interface QueueDepth {
  readonly enqueued: number;
  readonly processing: number;
  readonly failed: number;
  readonly succeeded: number;
}
