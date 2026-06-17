using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using PulseOne.SharedKernel.Security;

namespace PulseOne.WebApi.Auth;

/// <summary>
/// Normalizes IdP-specific claims onto PulseOne's stable claim names (01-auth-module.md):
/// <list type="bullet">
///   <item><c>extension_tenant_id</c> (Azure AD B2C custom attribute) → <c>tenant_id</c>,
///         consumed by <c>TenantResolutionMiddleware</c>.</item>
///   <item><c>portal</c> (<c>tenant</c> | <c>host</c>) — passed through, validated.</item>
/// </list>
/// Runs on every request AFTER JWT validation. It only ADDS normalized claims when they are
/// absent; it never removes the originals and never invents a tenant — fail-closed, the
/// downstream middleware rejects the request if no tenant claim ends up present.
/// </summary>
public sealed class TenantClaimsTransformer : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        // Only transform authenticated principals; anonymous requests pass through untouched.
        if (principal.Identity?.IsAuthenticated != true)
            return Task.FromResult(principal);

        var identity = (ClaimsIdentity)principal.Identity;

        // tenant_id: prefer an already-normalized claim; otherwise map from the B2C attribute.
        if (principal.FindFirst(AuthClaimTypes.TenantId) is null)
        {
            var b2cTenant = principal.FindFirstValue(AuthClaimTypes.B2CTenantId);
            if (!string.IsNullOrWhiteSpace(b2cTenant))
                identity.AddClaim(new Claim(AuthClaimTypes.TenantId, b2cTenant));
        }

        // portal: normalize-by-presence only. We do NOT default a portal value — a token without
        // a portal claim is treated as a tenant principal by the host policy (which requires the
        // explicit portal=host claim), so omission can never grant host access.
        return Task.FromResult(principal);
    }
}
