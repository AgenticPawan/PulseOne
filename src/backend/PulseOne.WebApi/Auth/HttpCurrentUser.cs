using System.Security.Claims;
using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.Security;

namespace PulseOne.WebApi.Auth;

/// <summary>
/// <see cref="ICurrentUser"/> over the authenticated <see cref="ClaimsPrincipal"/> for the
/// current request. Reads the NORMALIZED claims produced by <see cref="TenantClaimsTransformer"/>,
/// so it is independent of the IdP's wire claim names. Scoped (one per request).
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public string UserId =>
        Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? Principal?.FindFirstValue(AuthClaimTypes.Subject)
        // Fail-closed: an audit stamp must never be written under an unknown identity.
        ?? throw new InvalidOperationException(
            "Current user accessed without an authenticated principal.");

    public string? TenantId => Principal?.FindFirstValue(AuthClaimTypes.TenantId);

    public bool IsHostOperator =>
        string.Equals(
            Principal?.FindFirstValue(AuthClaimTypes.Portal),
            AuthClaimValues.HostPortal,
            StringComparison.Ordinal)
        && Principal!.IsInRole(AuthClaimValues.PlatformOperatorRole);
}
