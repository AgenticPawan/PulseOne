using PulseOne.Application.Features.TenantPortal;
using PulseOne.SharedKernel.Paging;

namespace PulseOne.WebApi.Tests.Authorization;

/// <summary>
/// Test double for <see cref="ITenantPortalService"/>. The PBAC endpoint tests assert AUTHORIZATION,
/// not business behaviour, so the allow-path only needs deterministic, non-throwing, 2xx-shaped
/// responses with no database access. The deny/anon paths never reach this stub (authorization runs
/// first), exactly mirroring <c>StubHostAdminService</c>.
/// </summary>
public sealed class StubTenantPortalService : ITenantPortalService
{
    public Task<PagedResult<ReportSummaryDto>> ListReportsAsync(ReportListQuery query, CancellationToken ct = default) =>
        Task.FromResult(new PagedResult<ReportSummaryDto>([], 0, query.PageNumber, query.PageSize));

    public IReadOnlyList<ReportTypeDescriptorDto> GetReportTypes() => [];

    public Task<CreateReportResponse> CreateReportAsync(CreateReportRequest request, CancellationToken ct = default) =>
        Task.FromResult(new CreateReportResponse("report-stub"));

    public Task<ReportDownloadResponse?> GetReportDownloadAsync(string reportId, CancellationToken ct = default) =>
        Task.FromResult<ReportDownloadResponse?>(new ReportDownloadResponse("https://stub.local/sas"));

    public Task<bool> DeleteReportAsync(string reportId, CancellationToken ct = default) => Task.FromResult(true);

    public Task<DashboardSummaryDto> GetDashboardSummaryAsync(CancellationToken ct = default) =>
        Task.FromResult(new DashboardSummaryDto(0, 0, 0, 0, "Free"));

    public Task<IReadOnlyList<ActivityEntryDto>> GetRecentActivityAsync(int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ActivityEntryDto>>([]);

    public Task<IReadOnlyList<TeamMemberDto>> GetTeamAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TeamMemberDto>>([]);

    public IReadOnlyList<PermissionDescriptorDto> GetAssignablePermissions() => [];

    public Task InviteUserAsync(InviteUserRequest request, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> UpdatePermissionsAsync(
        string userId, IReadOnlyList<string> permissions, CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> DeactivateUserAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> ReactivateUserAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);

    public Task<CompanyProfileDto> GetProfileAsync(CancellationToken ct = default) =>
        Task.FromResult(new CompanyProfileDto("Stub Co", "ops@stub.local", "", null));

    public Task UpdateProfileAsync(CompanyProfileDto profile, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<NotificationPreferenceDto>> GetNotificationPreferencesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<NotificationPreferenceDto>>([]);

    public Task UpdateNotificationPreferencesAsync(
        IReadOnlyList<NotificationPreferenceDto> preferences, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<ApiKeySummaryDto>> GetApiKeysAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ApiKeySummaryDto>>([]);

    public Task<CreatedApiKeyDto> CreateApiKeyAsync(string name, CancellationToken ct = default) =>
        Task.FromResult(new CreatedApiKeyDto("key-stub", name, "sk_stub_secret"));

    public Task<bool> RevokeApiKeyAsync(string id, CancellationToken ct = default) => Task.FromResult(true);

    public string RequestDataExport() => "job-stub";

    public Task<bool> RequestAccountDeletionAsync(string confirmation, CancellationToken ct = default) =>
        Task.FromResult(true);
}
