using Microsoft.AspNetCore.Builder;

namespace PulseOne.WebApi.Auth;

/// <summary>
/// Endpoint sugar so minimal-API routes can demand a PBAC permission by name:
/// <code>app.MapGet("/reports", ...).RequirePermission(Permissions.Reports.View);</code>
/// Resolves to the dynamic <c>perm:{name}</c> policy backed by <c>PermissionRequirement</c>.
/// </summary>
public static class AuthorizationExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.RequireAuthorization(PermissionPolicyProvider.PolicyName(permission));
    }
}
