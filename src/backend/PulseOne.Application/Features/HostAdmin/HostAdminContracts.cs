namespace PulseOne.Application.Features.HostAdmin;

/// <summary>
/// Response/request DTOs for the host admin portal (blueprint §6, Module 1–4). These mirror the
/// Angular host-portal contracts under <c>/api/v1/host/</c>. They live in Application (free of
/// Infrastructure types) so the WebApi endpoints and the Infrastructure implementation share one
/// definition. Status values are surfaced as strings to keep the catalog enum out of this layer.
/// </summary>
public sealed record TenantSummaryDto(
    string TenantId,
    string Name,
    string Plan,
    string Shard,
    string Status,
    DateTimeOffset CreatedAtUtc);

public sealed record TenantDetailDto(
    string TenantId,
    string Name,
    string Plan,
    string Shard,
    string Status,
    DateTimeOffset CreatedAtUtc,
    string AdminEmail,
    string Region);

public sealed record TenantUserSummaryDto(
    string UserId,
    string Email,
    string Role,
    DateTimeOffset? LastLoginUtc);

public sealed record TenantStorageUsageDto(
    long UsedBytes,
    long QuotaBytes,
    int DocumentCount);

public sealed record TenantSubscriptionHistoryEntryDto(
    string SubscriptionId,
    string Plan,
    string Status,
    DateTimeOffset StartedUtc,
    DateTimeOffset? EndedUtc);

/// <summary>Payload for POST /api/v1/host/tenants.</summary>
public sealed record ProvisionTenantRequest(
    string TenantId,
    string CompanyName,
    string PlanTier,
    string AssignedShard,
    string AdminEmail);

public sealed record SubscriptionSummaryDto(
    string TenantId,
    string TenantName,
    string RazorpaySubscriptionId,
    string Plan,
    string Status,
    DateTimeOffset? NextBillingUtc,
    long AmountInPaise);

public sealed record SubscriptionMetricsDto(
    int ActiveSubscriptions,
    long MonthlyRecurringRevenueInPaise,
    double ChurnRatePercent,
    int PendingCancellations);

public sealed record AuditLogEntryDto(
    string Id,
    string TenantId,
    string UserId,
    string Action,
    string TableName,
    DateTimeOffset TimestampUtc,
    string Summary);

public sealed record QueueDepthDto(
    int Enqueued,
    int Processing,
    int Failed,
    int Succeeded);

/// <summary>Inbound query for the tenant list (GET /api/v1/host/tenants).</summary>
public sealed record TenantListQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? SearchTerm = null,
    string? Status = null,
    string SortColumn = "name",
    string SortOrder = "asc");

/// <summary>Inbound query for the global audit browser (GET /api/v1/host/audit).</summary>
public sealed record AuditQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? TenantId = null,
    string? UserId = null,
    string? Action = null,
    string? TableName = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);
