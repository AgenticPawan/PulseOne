using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using PulseOne.Application.Authorization;

namespace PulseOne.WebApi.Auth;

/// <summary>
/// Supplies one authorization policy per PBAC permission on demand, so endpoints can require a
/// permission by name (e.g. <c>RequirePermission(Permissions.Reports.Export)</c>) without
/// registering every policy up-front. Policies whose name starts with <see cref="Prefix"/> are
/// built dynamically as a <see cref="PermissionRequirement"/>; all other names (e.g.
/// <c>HostOperatorsOnly</c>) fall through to the default provider.
///
/// Registering via a loop "one policy per permission" is also supported eagerly in Program.cs;
/// this provider additionally covers any permission referenced via the prefix convention.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : IAuthorizationPolicyProvider
{
    public const string Prefix = "perm:";

    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            var permission = policyName[Prefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }

    /// <summary>The policy name for a permission. Pair with <c>RequireAuthorization(PolicyName(...))</c>.</summary>
    public static string PolicyName(string permission) => Prefix + permission;
}
