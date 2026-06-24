using System.Net;
using System.Net.Http.Json;
using PulseOne.CoreDomain.Authorization;
using Xunit;

namespace PulseOne.WebApi.Tests.Authorization;

/// <summary>
/// PBAC proof for the tenant-portal endpoint group (blueprint Module 3 / 02-pbac-permissions.md).
/// Drives the REAL per-permission policies over the REAL <c>/api/v1/{reports,dashboard,team,settings}</c>
/// routes via <see cref="TenantPortalWebApplicationFactory"/>. The properties under test:
/// <list type="number">
///   <item>a tenant user WITHOUT the route's permission is rejected with 403;</item>
///   <item>a tenant user WITH exactly that permission is admitted (the request reaches the handler);</item>
///   <item>an anonymous caller is hard-rejected before the resource;</item>
///   <item>holding a DIFFERENT permission does not open the route — permissions don't bleed across.</item>
/// </list>
/// </summary>
[Trait("Category", "Authorization")]
public sealed class TenantPortalPbacTests(TenantPortalWebApplicationFactory factory)
    : IClassFixture<TenantPortalWebApplicationFactory>
{
    private const string Tenant = "acme";

    private readonly TenantPortalWebApplicationFactory _factory = factory;

    // (method, path, required permission). One row per route so a future route added without the
    // correct RequirePermission is caught.
    public static TheoryData<string, string, string> Routes() => new()
    {
        { "GET", "/api/v1/reports", Permissions.Reports.View },
        { "GET", "/api/v1/reports/types", Permissions.Reports.View },
        { "POST", "/api/v1/reports", Permissions.Reports.Export },
        { "GET", "/api/v1/reports/r1/download", Permissions.Reports.View },
        { "DELETE", "/api/v1/reports/r1", Permissions.Reports.Export },
        { "GET", "/api/v1/dashboard/summary", Permissions.Reports.View },
        { "GET", "/api/v1/dashboard/activity", Permissions.Reports.View },
        { "GET", "/api/v1/team", Permissions.Users.View },
        { "GET", "/api/v1/team/permissions", Permissions.Users.View },
        { "POST", "/api/v1/team/invitations", Permissions.Users.Manage },
        { "PUT", "/api/v1/team/u1/permissions", Permissions.Users.Manage },
        { "POST", "/api/v1/team/u1/deactivate", Permissions.Users.Manage },
        { "POST", "/api/v1/team/u1/reactivate", Permissions.Users.Manage },
        { "GET", "/api/v1/settings/profile", Permissions.Users.View },
        { "PUT", "/api/v1/settings/profile", Permissions.Users.Manage },
        { "GET", "/api/v1/settings/notifications", Permissions.Users.View },
        { "PUT", "/api/v1/settings/notifications", Permissions.Users.Manage },
        { "GET", "/api/v1/settings/api-keys", Permissions.Users.View },
        { "POST", "/api/v1/settings/api-keys", Permissions.Users.Manage },
        { "DELETE", "/api/v1/settings/api-keys/k1", Permissions.Users.Manage },
        { "POST", "/api/v1/settings/export", Permissions.Users.Manage },
        { "POST", "/api/v1/settings/account-deletion", Permissions.Users.Manage },
    };

    private static HttpRequestMessage Build(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
            request.Content = JsonContent.Create(new { }); // harmless body; deny/anon never read it
        return request;
    }

    // A tenant principal whose subdomain hint matches the tenant_id claim (TenantResolutionMiddleware
    // 403s on a mismatch), carrying the given comma-separated permission grant.
    private static void Authenticate(HttpRequestMessage request, string? permissions)
    {
        request.Headers.Add("X-Test-Authenticated", "true");
        request.Headers.Add("X-Test-Portal", "tenant");
        request.Headers.Add("X-Test-Tenant", Tenant);
        request.Headers.Add("X-Tenant-Hint", Tenant);
        if (permissions is not null)
            request.Headers.Add(HeaderDrivenPermissionService.HeaderName, permissions);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task TenantUserWithoutPermission_IsForbidden(string method, string path, string permission)
    {
        _ = permission; // the point of this case is that NO permission is granted
        var client = _factory.CreateClient();
        var request = Build(method, path);
        Authenticate(request, permissions: ""); // resolves the tenant, grants nothing

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task TenantUserWithRequiredPermission_IsAdmitted(string method, string path, string permission)
    {
        var client = _factory.CreateClient();
        var request = Build(method, path);
        Authenticate(request, permissions: permission);

        var response = await client.SendAsync(request);

        // Authorization passed and the request reached the (stubbed) handler. We assert the negative
        // property — never 401/403 — because individual routes legitimately return 200/202/204.
        Assert.True(
            response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden),
            $"{method} {path} with '{permission}' should be admitted but was {(int)response.StatusCode}.");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task AnonymousCaller_IsRejected(string method, string path, string permission)
    {
        _ = permission;
        var client = _factory.CreateClient();

        var response = await client.SendAsync(Build(method, path));

        // No credentials: rejected before the resource. TenantResolutionMiddleware (no tenant_id /
        // no hint) returns 400 ahead of the 401 challenge; either way the resource is unreachable.
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest,
            $"Anonymous {method} {path} should be rejected (401/400) but was {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task ReportsPermission_DoesNotOpen_TeamManagement()
    {
        var client = _factory.CreateClient();
        var request = Build("POST", "/api/v1/team/invitations"); // requires users.manage
        Authenticate(request, permissions: Permissions.Reports.View); // wrong permission

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UsersPermission_DoesNotOpen_ReportCreation()
    {
        var client = _factory.CreateClient();
        var request = Build("POST", "/api/v1/reports"); // requires reports.export
        Authenticate(request, permissions: Permissions.Users.View); // wrong permission

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
