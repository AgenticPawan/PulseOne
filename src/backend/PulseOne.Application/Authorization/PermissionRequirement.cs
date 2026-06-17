using Microsoft.AspNetCore.Authorization;

namespace PulseOne.Application.Authorization;

/// <summary>
/// Authorization requirement satisfied only when the current principal holds
/// <see cref="Permission"/> within the resolved tenant. One requirement instance backs the
/// dynamically-created <c>perm:{name}</c> policy (see <c>PermissionPolicyProvider</c>).
/// </summary>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
