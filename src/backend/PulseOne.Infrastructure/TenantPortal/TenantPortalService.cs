using System.Security.Cryptography;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using PulseOne.Application.Features.TenantPortal;
using PulseOne.CoreDomain.Authorization;
using PulseOne.CoreDomain.Entities;
using PulseOne.Infrastructure.Authorization;
using PulseOne.Infrastructure.Persistence;
using PulseOne.Infrastructure.Persistence.Catalog;
using PulseOne.SharedKernel.BackgroundJobs;
using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.Paging;

namespace PulseOne.Infrastructure.TenantPortal;

/// <summary>
/// Default <see cref="ITenantPortalService"/>. Runs inside the request's resolved tenant: the
/// injected <see cref="ApplicationDbContext"/> is bound to the caller's tenant by
/// <c>TenantResolutionMiddleware</c>, so the "Tenant" and "SoftDelete" named query filters enforce
/// isolation on every read/write here — there is no shard enumeration and no filter bypass (contrast
/// with the host admin service, which is the only sanctioned cross-tenant path). The Tenant Catalog
/// is read only to surface the tenant's plan tier (quota / current plan).
/// </summary>
public sealed class TenantPortalService(
    ApplicationDbContext db,
    TenantCatalogDbContext catalog,
    ICurrentUser currentUser,
    IBackgroundJobClient jobs) : ITenantPortalService
{
    // ---- Reports -----------------------------------------------------------------------------

    public async Task<PagedResult<ReportSummaryDto>> ListReportsAsync(
        ReportListQuery query, CancellationToken ct = default)
    {
        var q = db.Reports.AsNoTracking(); // "Tenant" + "SoftDelete" filters apply automatically.

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            q = q.Where(r => r.ReportName.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var stored = ToStoredStatus(query.Status);
            q = q.Where(r => r.Status == stored);
        }

        var descending = string.Equals(query.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);
        q = query.SortColumn?.ToLowerInvariant() switch
        {
            "reportname" => descending ? q.OrderByDescending(r => r.ReportName) : q.OrderBy(r => r.ReportName),
            "reporttype" => descending ? q.OrderByDescending(r => r.ReportType) : q.OrderBy(r => r.ReportType),
            "status" => descending ? q.OrderByDescending(r => r.Status) : q.OrderBy(r => r.Status),
            _ => descending ? q.OrderByDescending(r => r.CreatedAt) : q.OrderBy(r => r.CreatedAt),
        };

        var total = await q.CountAsync(ct);
        var rows = await q
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = rows.Select(r => new ReportSummaryDto(
            r.Id, r.ReportName, r.ReportType, ToClientStatus(r.Status), r.CreatedAt, SizeBytes: null)).ToList();

        return new PagedResult<ReportSummaryDto>(items, total, query.PageNumber, query.PageSize);
    }

    public IReadOnlyList<ReportTypeDescriptorDto> GetReportTypes()
    {
        // Keys are the engine type names the worker matches on (ReportProcessorJob → IReportEngine).
        var period = new ReportParameterDescriptorDto(
            "period", "Period", "select", Required: true,
            Options: ["Last 7 days", "Last 30 days", "This quarter", "This year"]);
        var name = new ReportParameterDescriptorDto("name", "Report name", "text", Required: false, Options: null);

        return
        [
            new ReportTypeDescriptorDto("Excel", "Data Export (Excel)", [name, period]),
            new ReportTypeDescriptorDto("Pdf", "Summary (PDF)", [name, period]),
        ];
    }

    public async Task<CreateReportResponse> CreateReportAsync(
        CreateReportRequest request, CancellationToken ct = default)
    {
        var reportType = string.Equals(request.ReportType, "Pdf", StringComparison.OrdinalIgnoreCase)
            ? "Pdf"
            : "Excel";

        var name = request.Parameters.TryGetValue("name", out var n) && !string.IsNullOrWhiteSpace(n)
            ? n.Trim()
            : $"{reportType} report {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}";

        var report = new Report { ReportName = name, ReportType = reportType, Status = "Pending" };
        db.Reports.Add(report);
        await db.SaveChangesAsync(ct); // stamps TenantId + audit row.

        // Fast-ack: hand the heavy generation to the worker, linking the producer trace, and return.
        var tenantId = db.CurrentTenantId;
        jobs.Enqueue<IReportGenerationJob>(
            j => j.ProcessAsync(report.Id, tenantId, TracedJobArgs.Capture(), CancellationToken.None));

        return new CreateReportResponse(report.Id);
    }

    public async Task<ReportDownloadResponse?> GetReportDownloadAsync(
        string reportId, CancellationToken ct = default)
    {
        var report = await db.Reports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId, ct);
        if (report is null || report.Status != "Completed" || string.IsNullOrWhiteSpace(report.OutputUrl))
            return null;

        return new ReportDownloadResponse(report.OutputUrl);
    }

    public async Task<bool> DeleteReportAsync(string reportId, CancellationToken ct = default)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, ct);
        if (report is null)
            return false;

        db.Reports.Remove(report); // converted to soft-delete by SaveChangesAsync.
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Dashboard ---------------------------------------------------------------------------

    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(CancellationToken ct = default)
    {
        var reportsGenerated = await db.Reports.AsNoTracking().CountAsync(ct);

        var deactivated = await db.TenantMembers.AsNoTracking()
            .Where(m => m.Status == "Deactivated")
            .Select(m => m.UserId)
            .ToListAsync(ct);

        var userIds = await db.TenantUserRoles.AsNoTracking()
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(ct);
        var activeUsers = userIds.Count(u => !deactivated.Contains(u));

        var tier = await TenantTierAsync(ct);

        return new DashboardSummaryDto(
            ActiveUsers: activeUsers,
            ReportsGenerated: reportsGenerated,
            // No blob-metering feed yet (same gap as the host portal); surfaced as 0 until wired in.
            StorageUsedBytes: 0,
            StorageQuotaBytes: QuotaBytes(tier),
            CurrentPlan: tier.ToString());
    }

    public async Task<IReadOnlyList<ActivityEntryDto>> GetRecentActivityAsync(
        int take, CancellationToken ct = default)
    {
        // AuditLog is excluded from the "Tenant" filter (so audit writes never recurse), so scope
        // explicitly to this tenant — CurrentTenantId is fail-closed (throws if unresolved).
        var tenantId = db.CurrentTenantId;
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.Timestamp)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);

        // ActorEmail surfaces the user id: IdP emails are not mirrored into the shard (documented gap).
        return rows.Select(a => new ActivityEntryDto(
            a.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            a.Action, $"{a.Action} {a.TableName}", a.UserId, a.Timestamp)).ToList();
    }

    // ---- Team --------------------------------------------------------------------------------

    public async Task<IReadOnlyList<TeamMemberDto>> GetTeamAsync(CancellationToken ct = default)
    {
        var grants = await db.TenantUserRoles.AsNoTracking().ToListAsync(ct);
        var roles = await db.TenantRoles.AsNoTracking().ToListAsync(ct);
        var members = await db.TenantMembers.AsNoTracking().ToListAsync(ct);
        var invites = await db.TeamInvitations.AsNoTracking()
            .Where(i => i.Status == "Pending").ToListAsync(ct);

        var roleById = roles.ToDictionary(r => r.Id);
        var memberByUser = members.ToDictionary(m => m.UserId);

        var userIds = grants.Select(g => g.UserId)
            .Concat(members.Select(m => m.UserId))
            .Distinct();

        var result = new List<TeamMemberDto>();
        foreach (var userId in userIds)
        {
            var userRoles = grants.Where(g => g.UserId == userId)
                .Select(g => roleById.GetValueOrDefault(g.RoleId))
                .Where(r => r is not null)
                .Select(r => r!)
                .ToList();

            var roleNames = userRoles.Select(r => r.Name).Distinct().ToList();
            var permissions = userRoles.SelectMany(r => r.Permissions).Distinct().ToList();
            var member = memberByUser.GetValueOrDefault(userId);

            result.Add(new TeamMemberDto(
                UserId: userId,
                Email: NonEmptyOr(member?.Email, userId),
                DisplayName: NonEmptyOr(member?.DisplayName, userId),
                Role: roleNames.Count > 0 ? string.Join(", ", roleNames) : "—",
                Status: member?.Status ?? "Active",
                LastLoginUtc: member?.LastLoginUtc,
                Permissions: permissions));
        }

        // Pending invitations show as "Invited" members keyed by the invitation id (no IdP subject yet).
        result.AddRange(invites.Select(i => new TeamMemberDto(
            i.Id, i.Email, i.Email, i.Role, "Invited", null, [])));

        return result.OrderBy(m => m.Status).ThenBy(m => m.Email).ToList();
    }

    public IReadOnlyList<PermissionDescriptorDto> GetAssignablePermissions() =>
        Permissions.All
            .Select(p => new PermissionDescriptorDto(p.Name, p.Description, p.Category))
            .ToList();

    public async Task InviteUserAsync(InviteUserRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            throw new ArgumentException("An invitation email is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Role))
            throw new ArgumentException("An invitation role is required.", nameof(request));

        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var invitation = new TeamInvitation
        {
            Email = request.Email.Trim(),
            Role = request.Role.Trim(),
            Token = token,
            Status = "Pending",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7),
        };
        db.TeamInvitations.Add(invitation);
        await db.SaveChangesAsync(ct);

        var tenantId = db.CurrentTenantId;
        jobs.Enqueue<IInvitationEmailJob>(
            j => j.SendAsync(tenantId, invitation.Id, invitation.Email, invitation.Role, token, CancellationToken.None));
    }

    public async Task<bool> UpdatePermissionsAsync(
        string userId, IReadOnlyList<string> permissions, CancellationToken ct = default)
    {
        if (!await UserExistsAsync(userId, ct))
            return false;

        // Only recognised permission constants are honoured (defense in depth against arbitrary grants).
        var valid = permissions.Where(Permissions.IsDefined).Distinct(StringComparer.Ordinal).ToList();

        // The user's effective permissions are set via a single managed per-user role, so PermissionService
        // (which resolves permissions from roles) stays authoritative. This REPLACES the user's grants.
        var roleName = $"custom:{userId}";
        var role = await db.TenantRoles.FirstOrDefaultAsync(r => r.Name == roleName, ct);
        if (role is null)
        {
            role = new TenantRole { Name = roleName, Permissions = valid, IsBuiltIn = false };
            db.TenantRoles.Add(role);
        }
        else
        {
            role.Permissions = valid;
        }
        await db.SaveChangesAsync(ct);

        var existing = await db.TenantUserRoles.Where(ur => ur.UserId == userId).ToListAsync(ct);
        db.TenantUserRoles.RemoveRange(existing.Where(ur => ur.RoleId != role.Id));
        if (!existing.Any(ur => ur.RoleId == role.Id))
            db.TenantUserRoles.Add(new TenantUserRole { UserId = userId, RoleId = role.Id });

        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<bool> DeactivateUserAsync(string userId, CancellationToken ct = default) =>
        SetMemberStatusAsync(userId, "Deactivated", ct);

    public Task<bool> ReactivateUserAsync(string userId, CancellationToken ct = default) =>
        SetMemberStatusAsync(userId, "Active", ct);

    private async Task<bool> SetMemberStatusAsync(string userId, string status, CancellationToken ct)
    {
        if (!await UserExistsAsync(userId, ct))
            return false;

        var member = await db.TenantMembers.FirstOrDefaultAsync(m => m.UserId == userId, ct);
        if (member is null)
        {
            member = new TenantMember
            {
                UserId = userId,
                Email = userId,
                DisplayName = userId,
                Status = status,
            };
            db.TenantMembers.Add(member);
        }
        else
        {
            member.Status = status;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Settings ----------------------------------------------------------------------------

    public async Task<CompanyProfileDto> GetProfileAsync(CancellationToken ct = default)
    {
        var p = await db.TenantProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        if (p is not null)
            return new CompanyProfileDto(p.CompanyName, p.ContactEmail, p.ContactPhone, p.LogoUrl);

        // No saved profile yet: seed the display name from the catalog so the form is not blank.
        var shard = await CatalogShardAsync(ct);
        return new CompanyProfileDto(shard?.Name ?? "", shard?.AdminEmail ?? "", "", null);
    }

    public async Task UpdateProfileAsync(CompanyProfileDto profile, CancellationToken ct = default)
    {
        var p = await db.TenantProfiles.FirstOrDefaultAsync(ct);
        if (p is null)
        {
            p = new TenantProfile();
            db.TenantProfiles.Add(p);
        }

        p.CompanyName = profile.CompanyName;
        p.ContactEmail = profile.ContactEmail;
        p.ContactPhone = profile.ContactPhone;
        p.LogoUrl = profile.LogoUrl;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NotificationPreferenceDto>> GetNotificationPreferencesAsync(
        CancellationToken ct = default)
    {
        var rows = await db.TenantNotificationSettings.AsNoTracking().ToListAsync(ct);
        if (rows.Count == 0)
            return DefaultNotificationPreferences(); // not persisted until the tenant saves.

        return rows
            .Select(r => new NotificationPreferenceDto(r.EventType, r.EventLabel, r.Email, r.Sms, r.Whatsapp))
            .ToList();
    }

    public async Task UpdateNotificationPreferencesAsync(
        IReadOnlyList<NotificationPreferenceDto> preferences, CancellationToken ct = default)
    {
        var existing = await db.TenantNotificationSettings.ToListAsync(ct);
        var byType = existing.ToDictionary(r => r.EventType, StringComparer.Ordinal);

        foreach (var pref in preferences)
        {
            if (byType.TryGetValue(pref.EventType, out var row))
            {
                row.EventLabel = pref.EventLabel;
                row.Email = pref.Email;
                row.Sms = pref.Sms;
                row.Whatsapp = pref.Whatsapp;
            }
            else
            {
                db.TenantNotificationSettings.Add(new TenantNotificationSetting
                {
                    EventType = pref.EventType,
                    EventLabel = pref.EventLabel,
                    Email = pref.Email,
                    Sms = pref.Sms,
                    Whatsapp = pref.Whatsapp,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ApiKeySummaryDto>> GetApiKeysAsync(CancellationToken ct = default)
    {
        var rows = await db.ApiKeys.AsNoTracking() // "SoftDelete" filter hides revoked keys.
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

        return rows
            .Select(k => new ApiKeySummaryDto(k.Id, k.Name, k.Prefix, k.CreatedAt, k.LastUsedUtc))
            .ToList();
    }

    public async Task<CreatedApiKeyDto> CreateApiKeyAsync(string name, CancellationToken ct = default)
    {
        // 32 bytes of CSPRNG entropy, url-safe. The plaintext is returned ONCE; only its hash is stored.
        var secret = "sk_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var prefix = secret[..12];
        var hash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret)));

        var key = new ApiKey
        {
            Name = string.IsNullOrWhiteSpace(name) ? "API key" : name.Trim(),
            Prefix = prefix,
            SecretHash = hash,
        };
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);

        return new CreatedApiKeyDto(key.Id, key.Name, secret);
    }

    public async Task<bool> RevokeApiKeyAsync(string id, CancellationToken ct = default)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (key is null)
            return false;

        db.ApiKeys.Remove(key); // soft-delete: the row is retained for the audit trail.
        await db.SaveChangesAsync(ct);
        return true;
    }

    public string RequestDataExport()
    {
        var tenantId = db.CurrentTenantId;
        return jobs.Enqueue<IDataExportJob>(
            j => j.ExportAsync(tenantId, currentUser.UserId, CancellationToken.None));
    }

    public async Task<bool> RequestAccountDeletionAsync(string confirmation, CancellationToken ct = default)
    {
        if (!string.Equals(confirmation, "DELETE", StringComparison.Ordinal))
            return false;

        // Record the request as a real, queryable audit row. AuditLog is excluded from the audit-capture
        // loop, so adding it directly does not recurse. The downstream retention/purge workflow (a
        // scheduled hard-delete after the cooling-off window) is owed — see the phase notes.
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = db.CurrentTenantId,
            UserId = currentUser.UserId,
            Action = "DeletionRequested",
            TableName = "Tenant",
            Timestamp = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---- helpers -----------------------------------------------------------------------------

    private async Task<bool> UserExistsAsync(string userId, CancellationToken ct) =>
        await db.TenantUserRoles.AnyAsync(ur => ur.UserId == userId, ct)
        || await db.TenantMembers.AnyAsync(m => m.UserId == userId, ct);

    private async Task<TenantShard?> CatalogShardAsync(CancellationToken ct)
    {
        var tenantId = db.CurrentTenantId;
        return await catalog.TenantShards.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
    }

    private async Task<TenantTier> TenantTierAsync(CancellationToken ct) =>
        (await CatalogShardAsync(ct))?.Tier ?? TenantTier.Free;

    private static string NonEmptyOr(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    // Storage entity uses "Pending"; the SPA's ReportStatus calls the queued state "Queued".
    private static string ToClientStatus(string stored) =>
        string.Equals(stored, "Pending", StringComparison.Ordinal) ? "Queued" : stored;

    private static string ToStoredStatus(string client) =>
        string.Equals(client, "Queued", StringComparison.OrdinalIgnoreCase) ? "Pending" : client;

    private static long QuotaBytes(TenantTier tier) => tier switch
    {
        TenantTier.Enterprise => 1_000L * 1024 * 1024 * 1024, // 1 TB
        TenantTier.Pro => 100L * 1024 * 1024 * 1024,          // 100 GB
        _ => 5L * 1024 * 1024 * 1024,                          // 5 GB
    };

    private static IReadOnlyList<NotificationPreferenceDto> DefaultNotificationPreferences() =>
    [
        new("report.completed", "Report completed", Email: true, Sms: false, Whatsapp: false),
        new("report.failed", "Report failed", Email: true, Sms: false, Whatsapp: false),
        new("billing.payment_succeeded", "Payment succeeded", Email: true, Sms: false, Whatsapp: false),
        new("billing.payment_failed", "Payment failed", Email: true, Sms: true, Whatsapp: false),
        new("team.member_invited", "Team member invited", Email: true, Sms: false, Whatsapp: false),
    ];
}
