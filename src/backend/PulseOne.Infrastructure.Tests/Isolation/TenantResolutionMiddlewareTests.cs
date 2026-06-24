using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using PulseOne.SharedKernel.Middleware;
using PulseOne.SharedKernel.MultiTenancy;
using Xunit;

namespace PulseOne.Infrastructure.Tests.Isolation;

/// <summary>
/// Defense-in-depth at the edge (blueprint §6.1). The middleware reconciles the Front Door subdomain
/// hint (<c>X-Tenant-Hint</c>) with the JWT <c>tenant_id</c> claim: a mismatch on an authenticated
/// request is a hijack attempt (403); an unknown/empty tenant is rejected (400); a valid tenant
/// resolves the fail-closed context and calls next.
/// </summary>
[Trait("Category", "Isolation")]
public sealed class TenantResolutionMiddlewareTests
{
    private static DefaultHttpContext BuildContext(
        string? tenantHint, string? claimTenant, bool authenticated)
    {
        var ctx = new DefaultHttpContext();
        if (tenantHint is not null)
            ctx.Request.Headers[TenantResolutionMiddleware.TenantHintHeader] = tenantHint;

        var claims = claimTenant is null
            ? []
            : new[] { new Claim(TenantResolutionMiddleware.TenantClaimType, claimTenant) };
        var identity = authenticated
            ? new ClaimsIdentity(claims, authenticationType: "test")
            : new ClaimsIdentity(claims); // no auth type => IsAuthenticated == false
        ctx.User = new ClaimsPrincipal(identity);
        return ctx;
    }

    private static ITenantCatalog CatalogKnowing(params string[] knownTenants)
    {
        var catalog = Substitute.For<ITenantCatalog>();
        catalog.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => knownTenants.Contains(call.Arg<string>()));
        return catalog;
    }

    [Fact]
    public async Task Authenticated_user_with_mismatched_claim_gets_403()
    {
        var nextCalled = false;
        var middleware = new TenantResolutionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var ctx = BuildContext(tenantHint: "contoso", claimTenant: "fabrikam", authenticated: true);
        var tenant = new TenantContext();

        await middleware.Invoke(ctx, tenant, CatalogKnowing("contoso", "fabrikam"));

        Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
        Assert.False(nextCalled);
        Assert.False(tenant.IsResolved); // never resolved on the reject path
    }

    [Fact]
    public async Task Unknown_tenant_id_gets_400()
    {
        var nextCalled = false;
        var middleware = new TenantResolutionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var ctx = BuildContext(tenantHint: "ghost", claimTenant: "ghost", authenticated: true);
        var tenant = new TenantContext();

        await middleware.Invoke(ctx, tenant, CatalogKnowing("contoso"));

        Assert.Equal(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);
        Assert.False(nextCalled);
        Assert.False(tenant.IsResolved);
    }

    [Fact]
    public async Task Empty_tenant_gets_400()
    {
        var nextCalled = false;
        var middleware = new TenantResolutionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        // Unauthenticated, no hint and no claim => resolved is empty => 400.
        var ctx = BuildContext(tenantHint: null, claimTenant: null, authenticated: false);
        var tenant = new TenantContext();

        await middleware.Invoke(ctx, tenant, CatalogKnowing("contoso"));

        Assert.Equal(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task Valid_tenant_resolves_and_calls_next()
    {
        var nextCalled = false;
        var middleware = new TenantResolutionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var ctx = BuildContext(tenantHint: "contoso", claimTenant: "contoso", authenticated: true);
        var tenant = new TenantContext();

        await middleware.Invoke(ctx, tenant, CatalogKnowing("contoso"));

        Assert.True(nextCalled);
        Assert.True(tenant.IsResolved);
        Assert.Equal("contoso", tenant.TenantId);
        Assert.NotEqual(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_request_resolves_from_subdomain_hint()
    {
        // No claim, not authenticated: the mismatch check is skipped and resolution falls back to the
        // subdomain hint (used by pre-auth flows such as the login page bootstrap).
        var nextCalled = false;
        var middleware = new TenantResolutionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var ctx = BuildContext(tenantHint: "contoso", claimTenant: null, authenticated: false);
        var tenant = new TenantContext();

        await middleware.Invoke(ctx, tenant, CatalogKnowing("contoso"));

        Assert.True(nextCalled);
        Assert.True(tenant.IsResolved);
        Assert.Equal("contoso", tenant.TenantId);
    }
}
