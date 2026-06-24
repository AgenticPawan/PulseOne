using System.Text.Json;
using Hangfire;
using PulseOne.Application.Features.HostAdmin;
using PulseOne.SharedKernel.Security;

namespace PulseOne.WebApi.Endpoints;

/// <summary>
/// Host admin portal endpoints (blueprint §6, Modules 1–4). Every route is gated server-side by the
/// <see cref="AuthorizationPolicies.HostOperatorsOnly"/> policy — the Angular router guard is UI-only
/// (security rule #4). Host operators carry no tenant_id, so this traffic is intentionally exempt
/// from <c>TenantResolutionMiddleware</c> (see <c>Program.cs</c>); cross-tenant data access is
/// brokered exclusively by <see cref="IHostAdminService"/>.
/// </summary>
public static class HostEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapHostEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var host = app.MapGroup("/api/v1/host")
            .RequireAuthorization(AuthorizationPolicies.HostOperatorsOnly);

        MapTenantEndpoints(host);
        MapSubscriptionEndpoints(host);
        MapAuditEndpoints(host);
        MapSystemEndpoints(host);

        return app;
    }

    // ---- Module 1: tenant lifecycle ----------------------------------------------------------
    private static void MapTenantEndpoints(RouteGroupBuilder host)
    {
        host.MapGet("/tenants", async (
            IHostAdminService svc, CancellationToken ct,
            int pageNumber = 1, int pageSize = 20, string? searchTerm = null,
            string? status = null, string sortColumn = "name", string sortOrder = "asc") =>
            Results.Ok(await svc.ListTenantsAsync(
                new TenantListQuery(pageNumber, pageSize, searchTerm, status, sortColumn, sortOrder), ct)));

        host.MapGet("/tenants/{tenantId}", async (
                string tenantId, IHostAdminService svc, CancellationToken ct) =>
            await svc.GetTenantAsync(tenantId, ct) is { } detail
                ? Results.Ok(detail)
                : Results.NotFound());

        host.MapPost("/tenants", async (
            ProvisionTenantRequest request, IHostAdminService svc, CancellationToken ct) =>
        {
            try
            {
                var detail = await svc.ProvisionTenantAsync(request, ct);
                return Results.Created($"/api/v1/host/tenants/{detail.TenantId}", detail);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        host.MapPost("/tenants/{tenantId}/suspend", async (
                string tenantId, IHostAdminService svc, CancellationToken ct) =>
            await svc.SuspendTenantAsync(tenantId, ct) ? Results.Ok() : Results.NotFound());

        host.MapPost("/tenants/{tenantId}/reactivate", async (
                string tenantId, IHostAdminService svc, CancellationToken ct) =>
            await svc.ReactivateTenantAsync(tenantId, ct) ? Results.Ok() : Results.NotFound());

        host.MapGet("/tenants/{tenantId}/users", (
                string tenantId, IHostAdminService svc, CancellationToken ct) =>
            NotFoundOnMissingTenant(() => svc.GetTenantUsersAsync(tenantId, ct)));

        host.MapGet("/tenants/{tenantId}/storage", (
                string tenantId, IHostAdminService svc, CancellationToken ct) =>
            NotFoundOnMissingTenant(() => svc.GetTenantStorageAsync(tenantId, ct)));

        host.MapGet("/tenants/{tenantId}/subscriptions", (
                string tenantId, IHostAdminService svc, CancellationToken ct) =>
            NotFoundOnMissingTenant(() => svc.GetTenantSubscriptionsAsync(tenantId, ct)));

        host.MapGet("/tenants/{tenantId}/audit", (
                string tenantId, IHostAdminService svc, CancellationToken ct,
                int pageNumber = 1, int pageSize = 20) =>
            NotFoundOnMissingTenant(() => svc.GetTenantAuditAsync(tenantId, pageNumber, pageSize, ct)));
    }

    // ---- Module 2: subscriptions -------------------------------------------------------------
    private static void MapSubscriptionEndpoints(RouteGroupBuilder host)
    {
        host.MapGet("/subscriptions", async (
                IHostAdminService svc, CancellationToken ct, int pageNumber = 1, int pageSize = 20) =>
            Results.Ok(await svc.ListSubscriptionsAsync(pageNumber, pageSize, ct)));

        host.MapGet("/subscriptions/metrics", async (IHostAdminService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetSubscriptionMetricsAsync(ct)));

        host.MapPost("/subscriptions/{subscriptionId}/extend-trial", async (
                string subscriptionId, ExtendTrialRequest body, IHostAdminService svc, CancellationToken ct) =>
            await svc.ExtendTrialAsync(subscriptionId, body.Days, ct) ? Results.Ok() : Results.NotFound());

        host.MapPost("/subscriptions/{subscriptionId}/discount", async (
                string subscriptionId, ApplyDiscountRequest body, IHostAdminService svc, CancellationToken ct) =>
            await svc.ApplyDiscountAsync(subscriptionId, body.Percent, ct) ? Results.Ok() : Results.NotFound());

        host.MapPost("/subscriptions/{subscriptionId}/cancel", async (
                string subscriptionId, IHostAdminService svc, CancellationToken ct) =>
            await svc.CancelSubscriptionAsync(subscriptionId, ct) ? Results.Ok() : Results.NotFound());
    }

    // ---- Module 3: cross-tenant audit browser ------------------------------------------------
    private static void MapAuditEndpoints(RouteGroupBuilder host)
    {
        host.MapGet("/audit", async (
            IHostAdminService svc, CancellationToken ct,
            int pageNumber = 1, int pageSize = 20, string? tenantId = null, string? userId = null,
            string? action = null, string? tableName = null,
            DateTimeOffset? from = null, DateTimeOffset? to = null) =>
            Results.Ok(await svc.SearchAuditAsync(
                new AuditQuery(pageNumber, pageSize, tenantId, userId, action, tableName, from, to), ct)));

        // Heavy export runs off-request: enqueue and return the Hangfire job id (fast-ack pattern).
        host.MapPost("/audit/export", (AuditQuery filters, IBackgroundJobClient jobs) =>
        {
            var filtersJson = JsonSerializer.Serialize(filters, JsonOptions);
            var jobId = jobs.Enqueue<IAuditExportJob>(j => j.ExportAsync(filtersJson, CancellationToken.None));
            return Results.Ok(new { jobId });
        });
    }

    // ---- Module 4: system health -------------------------------------------------------------
    private static void MapSystemEndpoints(RouteGroupBuilder host)
    {
        host.MapGet("/system/queue-depth", () =>
        {
            // Read straight off the Hangfire monitoring API — the producer shares the backplane.
            var stats = JobStorage.Current.GetMonitoringApi().GetStatistics();
            return Results.Ok(new QueueDepthDto(
                (int)stats.Enqueued, (int)stats.Processing, (int)stats.Failed, (int)stats.Succeeded));
        });
    }

    // Per-tenant reads throw KeyNotFoundException when the catalog has no such tenant; surface 404.
    private static async Task<IResult> NotFoundOnMissingTenant<T>(Func<Task<T>> read)
    {
        try
        {
            return Results.Ok(await read());
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }
}

/// <summary>Body for POST /api/v1/host/subscriptions/{id}/extend-trial.</summary>
public sealed record ExtendTrialRequest(int Days);

/// <summary>Body for POST /api/v1/host/subscriptions/{id}/discount.</summary>
public sealed record ApplyDiscountRequest(int Percent);
