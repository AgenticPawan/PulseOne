using PulseOne.SharedKernel.Paging;

namespace PulseOne.Application.Features.TenantPortal;

/// <summary>
/// Tenant portal application service (Phase 6). Backs every tenant-scoped <c>/api/v1/{reports,
/// dashboard,team,settings}</c> read/command. Unlike the host admin service, this runs entirely
/// within the request's resolved tenant: <c>TenantResolutionMiddleware</c> binds the
/// <c>ApplicationDbContext</c> to the caller's tenant, so the EF Core "Tenant" + "SoftDelete" named
/// query filters enforce isolation automatically — no shard enumeration, no filter bypass.
/// </summary>
public interface ITenantPortalService
{
    // ---- Reports -----------------------------------------------------------------------------
    Task<PagedResult<ReportSummaryDto>> ListReportsAsync(ReportListQuery query, CancellationToken ct = default);

    IReadOnlyList<ReportTypeDescriptorDto> GetReportTypes();

    /// <summary>Persists a Pending report and enqueues its generation; returns the new report id.</summary>
    Task<CreateReportResponse> CreateReportAsync(CreateReportRequest request, CancellationToken ct = default);

    /// <summary>The completed report's short-lived SAS URL, or null if missing/not yet completed.</summary>
    Task<ReportDownloadResponse?> GetReportDownloadAsync(string reportId, CancellationToken ct = default);

    /// <summary>Soft-deletes a report. False if it does not exist for this tenant.</summary>
    Task<bool> DeleteReportAsync(string reportId, CancellationToken ct = default);

    // ---- Dashboard ---------------------------------------------------------------------------
    Task<DashboardSummaryDto> GetDashboardSummaryAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ActivityEntryDto>> GetRecentActivityAsync(int take, CancellationToken ct = default);

    // ---- Team --------------------------------------------------------------------------------
    Task<IReadOnlyList<TeamMemberDto>> GetTeamAsync(CancellationToken ct = default);

    IReadOnlyList<PermissionDescriptorDto> GetAssignablePermissions();

    /// <summary>Creates a pending invitation and enqueues the invitation email.</summary>
    Task InviteUserAsync(InviteUserRequest request, CancellationToken ct = default);

    /// <summary>Sets a member's effective permissions via a managed per-user role. False if unknown.</summary>
    Task<bool> UpdatePermissionsAsync(string userId, IReadOnlyList<string> permissions, CancellationToken ct = default);

    Task<bool> DeactivateUserAsync(string userId, CancellationToken ct = default);

    Task<bool> ReactivateUserAsync(string userId, CancellationToken ct = default);

    // ---- Settings ----------------------------------------------------------------------------
    Task<CompanyProfileDto> GetProfileAsync(CancellationToken ct = default);

    Task UpdateProfileAsync(CompanyProfileDto profile, CancellationToken ct = default);

    Task<IReadOnlyList<NotificationPreferenceDto>> GetNotificationPreferencesAsync(CancellationToken ct = default);

    Task UpdateNotificationPreferencesAsync(
        IReadOnlyList<NotificationPreferenceDto> preferences, CancellationToken ct = default);

    Task<IReadOnlyList<ApiKeySummaryDto>> GetApiKeysAsync(CancellationToken ct = default);

    /// <summary>Mints an API key; the plaintext secret in the result is returned once and never stored.</summary>
    Task<CreatedApiKeyDto> CreateApiKeyAsync(string name, CancellationToken ct = default);

    Task<bool> RevokeApiKeyAsync(string id, CancellationToken ct = default);

    /// <summary>Enqueues a full-data export background job; returns the tracking job id.</summary>
    string RequestDataExport();

    /// <summary>Starts the account-deletion workflow (soft-delete + retention). False if confirmation fails.</summary>
    Task<bool> RequestAccountDeletionAsync(string confirmation, CancellationToken ct = default);
}
