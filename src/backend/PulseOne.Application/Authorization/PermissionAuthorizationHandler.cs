using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PulseOne.SharedKernel.Security;

namespace PulseOne.Application.Authorization;

/// <summary>
/// Evaluates a <see cref="PermissionRequirement"/> against the current principal.
///
/// Rules (02-pbac-permissions.md):
/// <list type="number">
///   <item>Host operators (<c>portal=host</c>) bypass tenant PBAC entirely.</item>
///   <item>Otherwise the permission is checked async, scoped to the principal's
///         <c>tenant_id</c> claim — a permission in tenant A grants nothing in tenant B.</item>
/// </list>
/// Fail-closed: on a missing user id, missing tenant, or any negative answer the handler simply
/// does not call <see cref="AuthorizationHandlerContext.Succeed"/>, leaving the requirement unmet.
/// </summary>
public sealed class PermissionAuthorizationHandler(IPermissionService permissions)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        var user = context.User;

        // Unauthenticated principals can never satisfy a permission requirement.
        if (user.Identity?.IsAuthenticated != true)
            return;

        // Rule 1: host operators bypass tenant-scoped PBAC. The authoritative host boundary is
        // still the server-side HostOperatorsOnly policy on host endpoints; this only governs
        // permission checks so platform operators are not blocked by tenant role tables.
        if (string.Equals(
                user.FindFirst(AuthClaimTypes.Portal)?.Value,
                AuthClaimValues.HostPortal,
                StringComparison.Ordinal))
        {
            context.Succeed(requirement);
            return;
        }

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? user.FindFirst(AuthClaimTypes.Subject)?.Value;
        var tenantId = user.FindFirst(AuthClaimTypes.TenantId)?.Value;

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tenantId))
            return; // fail-closed: cannot scope the check, so deny.

        // The resource may carry the request's CancellationToken when invoked from an endpoint.
        var ct = context.Resource as CancellationToken? ?? default;

        if (await permissions.HasPermissionAsync(userId, tenantId, requirement.Permission, ct))
            context.Succeed(requirement);
    }
}
