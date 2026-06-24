namespace PulseOne.Application.Features.TenantPortal;

/// <summary>
/// Request/response DTOs for the tenant portal (Phase 6, blueprint §6 tenant modules). These mirror
/// the Angular contracts in <c>client-app/src/app/core/models/tenant-models.ts</c> field-for-field
/// (the Minimal API uses camelCase JSON, so PascalCase here maps to the camelCase the SPA expects).
/// They live in Application — free of Infrastructure/EF types — so the WebApi endpoints and the
/// Infrastructure implementation share one definition. Every one is served ONLY through a
/// tenant-scoped endpoint; the authoritative isolation boundary is the EF Core named query filters.
/// </summary>

// ---- Reports ---------------------------------------------------------------------------------

public sealed record ReportSummaryDto(
    string Id,
    string ReportName,
    string ReportType,
    string Status,
    DateTimeOffset CreatedAtUtc,
    long? SizeBytes);

public sealed record ReportParameterDescriptorDto(
    string Key,
    string Label,
    string Kind,
    bool Required,
    IReadOnlyList<string>? Options);

public sealed record ReportTypeDescriptorDto(
    string Key,
    string Label,
    IReadOnlyList<ReportParameterDescriptorDto> Parameters);

/// <summary>Payload for POST /api/v1/reports.</summary>
public sealed record CreateReportRequest(
    string ReportType,
    Dictionary<string, string> Parameters);

/// <summary>Response for POST /api/v1/reports — the id to track over the SignalR hub.</summary>
public sealed record CreateReportResponse(string ReportId);

/// <summary>Response for GET /api/v1/reports/{id}/download — a short-lived SAS URL.</summary>
public sealed record ReportDownloadResponse(string DownloadUrl);

/// <summary>Inbound query for the reports grid (GET /api/v1/reports).</summary>
public sealed record ReportListQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? SearchTerm = null,
    string? Status = null,
    string SortColumn = "createdAtUtc",
    string SortOrder = "desc");

// ---- Dashboard -------------------------------------------------------------------------------

public sealed record DashboardSummaryDto(
    int ActiveUsers,
    int ReportsGenerated,
    long StorageUsedBytes,
    long StorageQuotaBytes,
    string CurrentPlan);

public sealed record ActivityEntryDto(
    string Id,
    string Action,
    string Summary,
    string ActorEmail,
    DateTimeOffset TimestampUtc);

// ---- Team ------------------------------------------------------------------------------------

public sealed record TeamMemberDto(
    string UserId,
    string Email,
    string DisplayName,
    string Role,
    string Status,
    DateTimeOffset? LastLoginUtc,
    IReadOnlyList<string> Permissions);

public sealed record PermissionDescriptorDto(
    string Key,
    string Label,
    string Category);

/// <summary>Payload for POST /api/v1/team/invitations.</summary>
public sealed record InviteUserRequest(string Email, string Role);

/// <summary>Payload for PUT /api/v1/team/{userId}/permissions.</summary>
public sealed record UpdatePermissionsRequest(IReadOnlyList<string> Permissions);

// ---- Settings --------------------------------------------------------------------------------

public sealed record CompanyProfileDto(
    string CompanyName,
    string ContactEmail,
    string ContactPhone,
    string? LogoUrl);

public sealed record NotificationPreferenceDto(
    string EventType,
    string EventLabel,
    bool Email,
    bool Sms,
    bool Whatsapp);

/// <summary>Payload for PUT /api/v1/settings/notifications.</summary>
public sealed record UpdateNotificationPreferencesRequest(
    IReadOnlyList<NotificationPreferenceDto> Preferences);

public sealed record ApiKeySummaryDto(
    string Id,
    string Name,
    string Prefix,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastUsedUtc);

/// <summary>Returned ONCE on creation — carries the plaintext secret, which is never persisted.</summary>
public sealed record CreatedApiKeyDto(string Id, string Name, string Secret);

/// <summary>Payload for POST /api/v1/settings/api-keys.</summary>
public sealed record CreateApiKeyRequest(string Name);

/// <summary>Payload for POST /api/v1/settings/account-deletion.</summary>
public sealed record AccountDeletionRequest(string Confirmation);
