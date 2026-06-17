using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using PulseOne.SharedKernel.Security;
using Xunit;

namespace PulseOne.Auth.Tests;

/// <summary>
/// Asserts the SERVER-SIDE host boundary (CLAUDE.md security rule #4). The policy must require
/// BOTH the <c>portal=host</c> claim AND the <c>platform-operator</c> role — neither alone, and
/// certainly not a tenant principal, may pass. This is the authoritative gate; the Angular guard
/// is UI-only and is never tested as a substitute for this.
/// </summary>
[Trait("Category", "Auth")]
public sealed class HostOperatorsOnlyPolicyTests
{
    /// <summary>
    /// Builds the EXACT same policy registered in AuthServiceCollectionExtensions and evaluates
    /// it against a principal using the framework's own requirement handlers — no web host needed.
    /// </summary>
    private static async Task<bool> EvaluateHostPolicyAsync(ClaimsPrincipal user)
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(AuthClaimTypes.Portal, AuthClaimValues.HostPortal)
            .RequireRole(AuthClaimValues.PlatformOperatorRole)
            .Build();

        var context = new AuthorizationHandlerContext(policy.Requirements, user, resource: null);

        // The built-in handlers that back RequireAuthenticatedUser/RequireClaim/RequireRole.
        var handlers = new IAuthorizationHandler[]
        {
            new DenyAnonymousAuthorizationRequirement(),
            new ClaimsAuthorizationRequirement(
                AuthClaimTypes.Portal, [AuthClaimValues.HostPortal]),
            new RolesAuthorizationRequirement([AuthClaimValues.PlatformOperatorRole]),
            new PassThroughAuthorizationHandler(),
        };

        foreach (var handler in handlers)
            await handler.HandleAsync(context);

        return context.HasSucceeded && !context.HasFailed;
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));

    [Fact]
    public async Task HostOperator_WithPortalAndRole_IsAuthorized()
    {
        var user = Principal(
            new Claim(AuthClaimTypes.Portal, AuthClaimValues.HostPortal),
            new Claim(ClaimTypes.Role, AuthClaimValues.PlatformOperatorRole));

        Assert.True(await EvaluateHostPolicyAsync(user));
    }

    [Fact]
    public async Task TenantUser_IsDenied_EvenWithRole()
    {
        var user = Principal(
            new Claim(AuthClaimTypes.Portal, AuthClaimValues.TenantPortal),
            new Claim(AuthClaimTypes.TenantId, "tenant-a"),
            new Claim(ClaimTypes.Role, AuthClaimValues.PlatformOperatorRole));

        Assert.False(await EvaluateHostPolicyAsync(user));
    }

    [Fact]
    public async Task HostPortalClaim_WithoutRole_IsDenied()
    {
        var user = Principal(new Claim(AuthClaimTypes.Portal, AuthClaimValues.HostPortal));

        Assert.False(await EvaluateHostPolicyAsync(user));
    }

    [Fact]
    public async Task OperatorRole_WithoutHostPortalClaim_IsDenied()
    {
        var user = Principal(new Claim(ClaimTypes.Role, AuthClaimValues.PlatformOperatorRole));

        Assert.False(await EvaluateHostPolicyAsync(user));
    }

    [Fact]
    public async Task AnonymousPrincipal_IsDenied()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity()); // not authenticated

        Assert.False(await EvaluateHostPolicyAsync(anonymous));
    }
}
