using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace PulseOne.WebApi.Tests.Authorization;

/// <summary>
/// The AUTHORITATIVE host-boundary proof (CLAUDE.md security rule #4, blueprint §7.3). Drives the REAL
/// <c>HostOperatorsOnly</c> policy over the REAL <c>/api/v1/host/*</c> endpoint group via
/// <see cref="HostBoundaryWebApplicationFactory"/>. A tenant principal — even one carrying the operator
/// role — MUST be rejected server-side; the Angular guard is UI-only and is never the gate. The deny
/// cases never reach <see cref="StubHostAdminService"/> because authorization runs first.
/// </summary>
[Trait("Category", "Authorization")]
public sealed class HostBoundaryTests(HostBoundaryWebApplicationFactory factory)
    : IClassFixture<HostBoundaryWebApplicationFactory>
{
    private readonly HostBoundaryWebApplicationFactory _factory = factory;

    // Every host route surface (method + path + a representative body where required). The boundary
    // is enforced by the group-level RequireAuthorization, so covering one route per shape is enough
    // to prove the policy is applied — but we enumerate the full set to guard against a future route
    // being added outside the protected group.
    public static TheoryData<string, string> HostRoutes() => new()
    {
        { "GET", "/api/v1/host/tenants" },
        { "GET", "/api/v1/host/tenants/acme" },
        { "POST", "/api/v1/host/tenants" },
        { "POST", "/api/v1/host/tenants/acme/suspend" },
        { "POST", "/api/v1/host/tenants/acme/reactivate" },
        { "GET", "/api/v1/host/tenants/acme/users" },
        { "GET", "/api/v1/host/tenants/acme/storage" },
        { "GET", "/api/v1/host/tenants/acme/subscriptions" },
        { "GET", "/api/v1/host/tenants/acme/audit" },
        { "GET", "/api/v1/host/subscriptions" },
        { "GET", "/api/v1/host/subscriptions/metrics" },
        { "POST", "/api/v1/host/subscriptions/sub_1/extend-trial" },
        { "POST", "/api/v1/host/subscriptions/sub_1/discount" },
        { "POST", "/api/v1/host/subscriptions/sub_1/cancel" },
        { "GET", "/api/v1/host/audit" },
        { "POST", "/api/v1/host/audit/export" },
        { "GET", "/api/v1/host/system/queue-depth" },
    };

    private static HttpRequestMessage Build(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = JsonContent.Create(new { }); // harmless body for POST routes
        return request;
    }

    [Theory]
    [MemberData(nameof(HostRoutes))]
    public async Task TenantPrincipal_IsForbidden_OnEveryHostRoute(string method, string path)
    {
        var client = _factory.CreateClient();
        var request = Build(method, path);
        // A tenant admin carrying the operator role but portal=tenant — must STILL be rejected.
        request.Headers.Add("X-Test-Authenticated", "true");
        request.Headers.Add("X-Test-Portal", "tenant");
        request.Headers.Add("X-Test-Role", "platform-operator");
        request.Headers.Add("X-Test-Tenant", "acme");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(HostRoutes))]
    public async Task AnonymousPrincipal_IsRejected_OnEveryHostRoute(string method, string path)
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(Build(method, path));

        // No credentials => the request is rejected BEFORE the resource. In this pipeline an
        // unauthenticated /api/* request is also caught by TenantResolutionMiddleware (no tenant_id
        // claim and no subdomain hint => 400) which runs ahead of the authorization challenge (401).
        // Either way the host resource is unreachable to an anonymous caller — the security property
        // under test. We assert "hard-rejected, never 2xx, never the resource".
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest,
            $"Anonymous {method} {path} should be rejected (401/400) but was {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task HostPortalWithoutOperatorRole_IsForbidden()
    {
        var client = _factory.CreateClient();
        var request = Build("GET", "/api/v1/host/tenants");
        request.Headers.Add("X-Test-Authenticated", "true");
        request.Headers.Add("X-Test-Portal", "host"); // correct portal, but NO platform-operator role

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HostOperator_WithPortalAndRole_IsAllowedThrough()
    {
        var client = _factory.CreateClient();
        var request = Build("GET", "/api/v1/host/tenants");
        request.Headers.Add("X-Test-Authenticated", "true");
        request.Headers.Add("X-Test-Portal", "host");
        request.Headers.Add("X-Test-Role", "platform-operator");

        var response = await client.SendAsync(request);

        // The policy passes; the request reaches the (stubbed) endpoint and returns 200 — proving the
        // boundary admits genuine operators while rejecting everyone else above.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
