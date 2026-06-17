using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PulseOne.Application.Authorization;
using PulseOne.CoreDomain.Authorization;
using PulseOne.SharedKernel.Security;
using Xunit;

namespace PulseOne.Auth.Tests;

/// <summary>
/// Unit tests for the PBAC handler. These are the security-critical assertions:
/// permissions are tenant-scoped, host operators bypass, and every ambiguous case is denied.
/// </summary>
[Trait("Category", "Auth")]
public sealed class PermissionAuthorizationHandlerTests
{
    private const string Permission = "reports.export";

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));

    private static async Task<bool> EvaluateAsync(IPermissionService service, ClaimsPrincipal user)
    {
        var requirement = new PermissionRequirement(Permission);
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);
        await new PermissionAuthorizationHandler(service).HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task TenantUser_WithGrantedPermission_Succeeds()
    {
        var user = Principal(
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim(AuthClaimTypes.TenantId, "tenant-a"));
        var service = new FakePermissionService(("user-1", "tenant-a", Permission));

        Assert.True(await EvaluateAsync(service, user));
    }

    [Fact]
    public async Task TenantUser_WithoutPermission_Fails()
    {
        var user = Principal(
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim(AuthClaimTypes.TenantId, "tenant-a"));
        var service = new FakePermissionService(); // no grants

        Assert.False(await EvaluateAsync(service, user));
    }

    [Fact]
    public async Task Permission_IsTenantScoped_GrantInTenantA_DeniedInTenantB()
    {
        // Same user id, granted only in tenant-a, but presents a tenant-b token.
        var user = Principal(
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim(AuthClaimTypes.TenantId, "tenant-b"));
        var service = new FakePermissionService(("user-1", "tenant-a", Permission));

        Assert.False(await EvaluateAsync(service, user));
    }

    [Fact]
    public async Task HostOperator_BypassesTenantPbac_Succeeds()
    {
        // No tenant claim, no DB grant — the portal=host claim alone passes.
        var user = Principal(new Claim(AuthClaimTypes.Portal, AuthClaimValues.HostPortal));
        var service = new FakePermissionService(); // never consulted

        Assert.True(await EvaluateAsync(service, user));
        Assert.False(service.WasQueried);
    }

    [Fact]
    public async Task Unauthenticated_Fails()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity()); // not authenticated
        var service = new FakePermissionService(("user-1", "tenant-a", Permission));

        Assert.False(await EvaluateAsync(service, anonymous));
    }

    [Fact]
    public async Task MissingTenantClaim_FailsClosed()
    {
        var user = Principal(new Claim(ClaimTypes.NameIdentifier, "user-1")); // no tenant_id
        var service = new FakePermissionService(("user-1", "tenant-a", Permission));

        Assert.False(await EvaluateAsync(service, user));
    }

    [Fact]
    public async Task MissingUserId_FailsClosed()
    {
        var user = Principal(new Claim(AuthClaimTypes.TenantId, "tenant-a")); // no NameIdentifier/sub
        var service = new FakePermissionService();

        Assert.False(await EvaluateAsync(service, user));
    }

    [Fact]
    public async Task SubClaim_UsedWhenNameIdentifierAbsent()
    {
        var user = Principal(
            new Claim(AuthClaimTypes.Subject, "user-sub"),
            new Claim(AuthClaimTypes.TenantId, "tenant-a"));
        var service = new FakePermissionService(("user-sub", "tenant-a", Permission));

        Assert.True(await EvaluateAsync(service, user));
    }

    [Fact]
    public void Permissions_Catalog_IsConsistent()
    {
        Assert.Contains(Permissions.Reports.Export, Permissions.AllNames);
        Assert.True(Permissions.IsDefined(Permissions.Billing.Manage));
        Assert.False(Permissions.IsDefined("does.not.exist"));
        Assert.Equal(Permissions.All.Count, Permissions.AllNames.Count);
    }
}
