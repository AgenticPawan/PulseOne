using PulseOne.Application.Features.HostAdmin;
using PulseOne.SharedKernel.Paging;

namespace PulseOne.WebApi.Tests.Authorization;

/// <summary>
/// Test double for <see cref="IHostAdminService"/>. The host-boundary tests assert AUTHORIZATION, not
/// business behaviour, so the allow-path test only needs a deterministic 200-shaped response with no
/// Azure SQL access. The deny-path tests never reach this stub (the policy rejects them first).
/// </summary>
public sealed class StubHostAdminService : IHostAdminService
{
    public Task<PagedResult<TenantSummaryDto>> ListTenantsAsync(TenantListQuery query, CancellationToken ct = default) =>
        Task.FromResult(new PagedResult<TenantSummaryDto>([], 0, query.PageNumber, query.PageSize));

    public Task<TenantDetailDto?> GetTenantAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<TenantDetailDto?>(null);

    public Task<TenantDetailDto> ProvisionTenantAsync(ProvisionTenantRequest request, CancellationToken ct = default) =>
        Task.FromResult(new TenantDetailDto(
            request.TenantId, request.CompanyName, request.PlanTier, request.AssignedShard,
            "Active", DateTimeOffset.UtcNow, request.AdminEmail, "westindia"));

    public Task<bool> SuspendTenantAsync(string tenantId, CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> ReactivateTenantAsync(string tenantId, CancellationToken ct = default) => Task.FromResult(true);

    public Task<IReadOnlyList<TenantUserSummaryDto>> GetTenantUsersAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TenantUserSummaryDto>>([]);

    public Task<TenantStorageUsageDto> GetTenantStorageAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult(new TenantStorageUsageDto(0, 0, 0));

    public Task<IReadOnlyList<TenantSubscriptionHistoryEntryDto>> GetTenantSubscriptionsAsync(
        string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TenantSubscriptionHistoryEntryDto>>([]);

    public Task<PagedResult<AuditLogEntryDto>> GetTenantAuditAsync(
        string tenantId, int pageNumber, int pageSize, CancellationToken ct = default) =>
        Task.FromResult(new PagedResult<AuditLogEntryDto>([], 0, pageNumber, pageSize));

    public Task<PagedResult<SubscriptionSummaryDto>> ListSubscriptionsAsync(
        int pageNumber, int pageSize, CancellationToken ct = default) =>
        Task.FromResult(new PagedResult<SubscriptionSummaryDto>([], 0, pageNumber, pageSize));

    public Task<SubscriptionMetricsDto> GetSubscriptionMetricsAsync(CancellationToken ct = default) =>
        Task.FromResult(new SubscriptionMetricsDto(0, 0, 0, 0));

    public Task<bool> ExtendTrialAsync(string razorpaySubscriptionId, int days, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<bool> ApplyDiscountAsync(string razorpaySubscriptionId, int percent, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<bool> CancelSubscriptionAsync(string razorpaySubscriptionId, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<PagedResult<AuditLogEntryDto>> SearchAuditAsync(AuditQuery query, CancellationToken ct = default) =>
        Task.FromResult(new PagedResult<AuditLogEntryDto>([], 0, query.PageNumber, query.PageSize));
}
