# Prompt: PBAC — Permission-Based Access Control

## Context
PulseOne uses Permission-Based Access Control (PBAC): permissions are the unit of authorization, roles are just named containers of permissions. This follows Module 3 (Admin Operations) from the blueprint.

## Task

### Permission Model (`PulseOne.CoreDomain`)
```csharp
public sealed record Permission(string Name, string Category, string Description);

public static class Permissions
{
    public static class Tenants  { public const string View = "tenants.view"; public const string Manage = "tenants.manage"; }
    public static class Reports  { public const string View = "reports.view"; public const string Export = "reports.export"; }
    public static class Billing  { public const string View = "billing.view"; public const string Manage = "billing.manage"; }
    public static class Users    { public const string View = "users.view";   public const string Manage = "users.manage"; }
    public static class Audit    { public const string View = "audit.view"; }
}
```

### Role-Permission Mapping (`PulseOne.Infrastructure`)
- `TenantRole` entity: `Id`, `TenantId`, `Name`, `Permissions (JSON column)`
- `TenantUserRole` join entity: `UserId`, `RoleId`, `TenantId`
- Default roles seeded per new tenant: `Admin` (all tenant permissions), `Viewer` (view-only)

### Authorization Handler
```csharp
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement;

public sealed class PermissionAuthorizationHandler(IPermissionService perms)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, PermissionRequirement req)
    {
        var tenantId = ctx.User.FindFirstValue("tenant_id");
        var userId   = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (await perms.HasPermissionAsync(userId, tenantId, req.Permission, ctx.Resource as CancellationToken? ?? default))
            ctx.Succeed(req);
    }
}
```

### Policy Registration
Register one policy per permission constant — use a loop, not manual `AddPolicy` calls for each.

## Output Locations
- `src/backend/PulseOne.CoreDomain/Authorization/Permissions.cs`
- `src/backend/PulseOne.Application/Authorization/`
- `src/backend/PulseOne.Infrastructure/Authorization/`
- `src/backend/PulseOne.WebApi/Auth/PermissionPolicyProvider.cs`

## Constraints
- PBAC permissions must be scoped to `tenantId` — a user with `reports.export` in tenant A does NOT have it in tenant B
- Host operators bypass tenant PBAC entirely (they have `portal=host` claim)
- Permission checks are always async (read from DB/cache, not just from claims)
