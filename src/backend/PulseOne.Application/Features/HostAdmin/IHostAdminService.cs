using PulseOne.SharedKernel.Paging;

namespace PulseOne.Application.Features.HostAdmin;

/// <summary>
/// Host admin portal application service (blueprint §6, Modules 1–4). Backs every
/// <c>/api/v1/host/*</c> read/command behind the <c>HostOperatorsOnly</c> policy.
/// </summary>
/// <remarks>
/// Host operators carry NO tenant_id, so the request-scoped <c>ITenantContext</c> would throw on
/// them (fail-closed). The implementation therefore builds host-scoped business-shard contexts on
/// demand: per-tenant reads run with the tenant query filter bound to that specific tenant, and
/// cross-tenant reads enumerate the distinct shards and bypass ONLY the named "Tenant" filter
/// (soft-delete still applies). This is the single sanctioned place where the tenant filter is
/// crossed, and it is reachable only behind the server-side host boundary (security rule #4).
/// </remarks>
public interface IHostAdminService
{
    // ---- Module 1: tenant lifecycle ----------------------------------------------------------
    Task<PagedResult<TenantSummaryDto>> ListTenantsAsync(TenantListQuery query, CancellationToken ct = default);

    Task<TenantDetailDto?> GetTenantAsync(string tenantId, CancellationToken ct = default);

    Task<TenantDetailDto> ProvisionTenantAsync(ProvisionTenantRequest request, CancellationToken ct = default);

    /// <summary>Suspends a tenant (Status→Suspended, routing disabled). False if the tenant is unknown.</summary>
    Task<bool> SuspendTenantAsync(string tenantId, CancellationToken ct = default);

    /// <summary>Reactivates a suspended tenant (Status→Active, routing re-enabled). False if unknown.</summary>
    Task<bool> ReactivateTenantAsync(string tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<TenantUserSummaryDto>> GetTenantUsersAsync(string tenantId, CancellationToken ct = default);

    Task<TenantStorageUsageDto> GetTenantStorageAsync(string tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<TenantSubscriptionHistoryEntryDto>> GetTenantSubscriptionsAsync(
        string tenantId, CancellationToken ct = default);

    Task<PagedResult<AuditLogEntryDto>> GetTenantAuditAsync(
        string tenantId, int pageNumber, int pageSize, CancellationToken ct = default);

    // ---- Module 2: subscriptions -------------------------------------------------------------
    Task<PagedResult<SubscriptionSummaryDto>> ListSubscriptionsAsync(
        int pageNumber, int pageSize, CancellationToken ct = default);

    Task<SubscriptionMetricsDto> GetSubscriptionMetricsAsync(CancellationToken ct = default);

    /// <summary>Manual trial extension. False if the subscription id is not found on any shard.</summary>
    Task<bool> ExtendTrialAsync(string razorpaySubscriptionId, int days, CancellationToken ct = default);

    /// <summary>Manual discount override. False if the subscription id is not found on any shard.</summary>
    Task<bool> ApplyDiscountAsync(string razorpaySubscriptionId, int percent, CancellationToken ct = default);

    /// <summary>Manual cancellation (Status→cancelled, audited). False if not found on any shard.</summary>
    Task<bool> CancelSubscriptionAsync(string razorpaySubscriptionId, CancellationToken ct = default);

    // ---- Module 3: cross-tenant audit browser ------------------------------------------------
    Task<PagedResult<AuditLogEntryDto>> SearchAuditAsync(AuditQuery query, CancellationToken ct = default);
}
