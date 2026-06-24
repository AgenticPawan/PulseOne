using PulseOne.Application.Features.TenantPortal;
using PulseOne.CoreDomain.Authorization;
using PulseOne.WebApi.Auth;

namespace PulseOne.WebApi.Endpoints;

/// <summary>
/// Tenant portal endpoints (Phase 6): reports, dashboard, team and settings under <c>/api/v1</c>.
/// Every route requires an authenticated tenant user and a specific PBAC permission (one policy per
/// permission via <see cref="PermissionPolicyProvider"/>). These routes are tenant-scoped:
/// <c>TenantResolutionMiddleware</c> binds the request to the caller's tenant before authorization,
/// so the <see cref="ITenantPortalService"/> reads/writes through the tenant-filtered
/// <c>ApplicationDbContext</c> — isolation is enforced server-side regardless of the SPA.
/// </summary>
public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var api = app.MapGroup("/api/v1");

        MapReportEndpoints(api);
        MapDashboardEndpoints(api);
        MapTeamEndpoints(api);
        MapSettingsEndpoints(api);

        return app;
    }

    // ---- Reports -----------------------------------------------------------------------------
    private static void MapReportEndpoints(RouteGroupBuilder api)
    {
        var reports = api.MapGroup("/reports");

        reports.MapGet("", async (
                ITenantPortalService svc, CancellationToken ct,
                int pageNumber = 1, int pageSize = 20, string? searchTerm = null,
                string? status = null, string sortColumn = "createdAtUtc", string sortOrder = "desc") =>
            Results.Ok(await svc.ListReportsAsync(
                new ReportListQuery(pageNumber, pageSize, searchTerm, status, sortColumn, sortOrder), ct)))
            .RequirePermission(Permissions.Reports.View);

        reports.MapGet("/types", (ITenantPortalService svc) => Results.Ok(svc.GetReportTypes()))
            .RequirePermission(Permissions.Reports.View);

        reports.MapPost("", async (
                CreateReportRequest request, ITenantPortalService svc, CancellationToken ct) =>
            Results.Ok(await svc.CreateReportAsync(request, ct)))
            .RequirePermission(Permissions.Reports.Export);

        reports.MapGet("/{reportId}/download", async (
                string reportId, ITenantPortalService svc, CancellationToken ct) =>
            await svc.GetReportDownloadAsync(reportId, ct) is { } dl ? Results.Ok(dl) : Results.NotFound())
            .RequirePermission(Permissions.Reports.View);

        reports.MapDelete("/{reportId}", async (
                string reportId, ITenantPortalService svc, CancellationToken ct) =>
            await svc.DeleteReportAsync(reportId, ct) ? Results.NoContent() : Results.NotFound())
            .RequirePermission(Permissions.Reports.Export);
    }

    // ---- Dashboard ---------------------------------------------------------------------------
    private static void MapDashboardEndpoints(RouteGroupBuilder api)
    {
        var dash = api.MapGroup("/dashboard");

        dash.MapGet("/summary", async (ITenantPortalService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetDashboardSummaryAsync(ct)))
            .RequirePermission(Permissions.Reports.View);

        dash.MapGet("/activity", async (ITenantPortalService svc, CancellationToken ct, int take = 10) =>
                Results.Ok(await svc.GetRecentActivityAsync(take, ct)))
            .RequirePermission(Permissions.Reports.View);
    }

    // ---- Team --------------------------------------------------------------------------------
    private static void MapTeamEndpoints(RouteGroupBuilder api)
    {
        var team = api.MapGroup("/team");

        team.MapGet("", async (ITenantPortalService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetTeamAsync(ct)))
            .RequirePermission(Permissions.Users.View);

        team.MapGet("/permissions", (ITenantPortalService svc) =>
                Results.Ok(svc.GetAssignablePermissions()))
            .RequirePermission(Permissions.Users.View);

        team.MapPost("/invitations", async (
                InviteUserRequest request, ITenantPortalService svc, CancellationToken ct) =>
            {
                await svc.InviteUserAsync(request, ct);
                return Results.Accepted();
            })
            .RequirePermission(Permissions.Users.Manage);

        team.MapPut("/{userId}/permissions", async (
                string userId, UpdatePermissionsRequest body, ITenantPortalService svc, CancellationToken ct) =>
            await svc.UpdatePermissionsAsync(userId, body.Permissions, ct) ? Results.Ok() : Results.NotFound())
            .RequirePermission(Permissions.Users.Manage);

        team.MapPost("/{userId}/deactivate", async (
                string userId, ITenantPortalService svc, CancellationToken ct) =>
            await svc.DeactivateUserAsync(userId, ct) ? Results.Ok() : Results.NotFound())
            .RequirePermission(Permissions.Users.Manage);

        team.MapPost("/{userId}/reactivate", async (
                string userId, ITenantPortalService svc, CancellationToken ct) =>
            await svc.ReactivateUserAsync(userId, ct) ? Results.Ok() : Results.NotFound())
            .RequirePermission(Permissions.Users.Manage);
    }

    // ---- Settings ----------------------------------------------------------------------------
    private static void MapSettingsEndpoints(RouteGroupBuilder api)
    {
        var settings = api.MapGroup("/settings");

        settings.MapGet("/profile", async (ITenantPortalService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetProfileAsync(ct)))
            .RequirePermission(Permissions.Users.View);

        settings.MapPut("/profile", async (
                CompanyProfileDto body, ITenantPortalService svc, CancellationToken ct) =>
            {
                await svc.UpdateProfileAsync(body, ct);
                return Results.Ok();
            })
            .RequirePermission(Permissions.Users.Manage);

        settings.MapGet("/notifications", async (ITenantPortalService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetNotificationPreferencesAsync(ct)))
            .RequirePermission(Permissions.Users.View);

        settings.MapPut("/notifications", async (
                UpdateNotificationPreferencesRequest body, ITenantPortalService svc, CancellationToken ct) =>
            {
                await svc.UpdateNotificationPreferencesAsync(body.Preferences, ct);
                return Results.Ok();
            })
            .RequirePermission(Permissions.Users.Manage);

        settings.MapGet("/api-keys", async (ITenantPortalService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetApiKeysAsync(ct)))
            .RequirePermission(Permissions.Users.View);

        settings.MapPost("/api-keys", async (
                CreateApiKeyRequest body, ITenantPortalService svc, CancellationToken ct) =>
            Results.Ok(await svc.CreateApiKeyAsync(body.Name, ct)))
            .RequirePermission(Permissions.Users.Manage);

        settings.MapDelete("/api-keys/{id}", async (
                string id, ITenantPortalService svc, CancellationToken ct) =>
            await svc.RevokeApiKeyAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .RequirePermission(Permissions.Users.Manage);

        // Heavy export runs off-request: enqueue and return the Hangfire job id (fast-ack pattern).
        settings.MapPost("/export", (ITenantPortalService svc) =>
                Results.Ok(new { jobId = svc.RequestDataExport() }))
            .RequirePermission(Permissions.Users.Manage);

        settings.MapPost("/account-deletion", async (
                AccountDeletionRequest body, ITenantPortalService svc, CancellationToken ct) =>
            await svc.RequestAccountDeletionAsync(body.Confirmation, ct)
                ? Results.Ok()
                : Results.BadRequest(new { error = "Confirmation text did not match." }))
            .RequirePermission(Permissions.Users.Manage);
    }

    /// <summary>Requires the PBAC permission via the per-permission policy convention.</summary>
    private static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PermissionPolicyProvider.PolicyName(permission));
}
