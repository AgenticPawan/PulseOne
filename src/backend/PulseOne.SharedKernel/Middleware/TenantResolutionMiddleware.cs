using Microsoft.AspNetCore.Http;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.SharedKernel.Middleware;

/// <summary>
/// Resolves the tenant for the request and enforces defense-in-depth (blueprint §6.1).
/// The Front Door sets <c>X-Tenant-Hint</c> from the subdomain; the JWT carries
/// <c>tenant_id</c>. For authenticated routes the two MUST agree — a mismatch is a
/// hijack attempt and is rejected with 403. Unknown/empty tenants are rejected with 400.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public const string TenantHintHeader = "X-Tenant-Hint";
    public const string TenantClaimType = "tenant_id";

    public async Task Invoke(HttpContext ctx, ITenantContext tenant, ITenantCatalog catalog)
    {
        var subdomainTenant = ctx.Request.Headers[TenantHintHeader].ToString(); // set by Front Door
        var claimTenant = ctx.User.FindFirst(TenantClaimType)?.Value;

        // For authenticated routes, the two MUST agree. A mismatch is a hijack attempt.
        if (ctx.User.Identity?.IsAuthenticated == true &&
            !string.Equals(subdomainTenant, claimTenant, StringComparison.Ordinal))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var resolved = claimTenant ?? subdomainTenant;
        if (string.IsNullOrWhiteSpace(resolved) ||
            !await catalog.ExistsAsync(resolved, ctx.RequestAborted))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        tenant.Resolve(resolved);
        await next(ctx);
    }
}
