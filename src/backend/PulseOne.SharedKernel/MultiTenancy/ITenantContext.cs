namespace PulseOne.SharedKernel.MultiTenancy;

/// <summary>
/// Thrown when tenant-scoped state is accessed before a tenant has been resolved,
/// or when an empty/invalid tenant id is supplied. This is the fail-closed signal:
/// the request must be rejected to prevent cross-tenant data exposure.
/// </summary>
public sealed class TenantResolutionException(string message) : Exception(message);

/// <summary>
/// Ambient tenant context for the current request scope. Fail-closed by contract:
/// <see cref="TenantId"/> throws <see cref="TenantResolutionException"/> when unresolved —
/// it MUST NEVER return "default" or <see cref="string.Empty"/>.
/// </summary>
public interface ITenantContext
{
    /// <summary>The resolved tenant id. Throws <see cref="TenantResolutionException"/> if unresolved.</summary>
    string TenantId { get; }

    /// <summary>True once a tenant has been successfully resolved for this scope.</summary>
    bool IsResolved { get; }

    /// <summary>
    /// Resolves the tenant for the current scope. Called once by
    /// <c>TenantResolutionMiddleware</c> after validating the tenant exists.
    /// </summary>
    void Resolve(string tenantId);
}
