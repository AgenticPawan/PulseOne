namespace PulseOne.SharedKernel.MultiTenancy;

/// <summary>
/// Fail-closed tenant context. Registered as <c>scoped</c> (one per HTTP request).
/// See blueprint §6.1: v1 defaulted to "default" on a DI miss, silently routing
/// reads/writes into a shared bucket (cross-tenant leak). v2 throws instead.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private string? _tenantId;

    public bool IsResolved => _tenantId is not null;

    public string TenantId => _tenantId
        ?? throw new TenantResolutionException(
            "Tenant accessed before resolution. Request rejected to prevent cross-tenant exposure.");

    public void Resolve(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new TenantResolutionException("Empty tenant id is not permitted.");

        _tenantId = tenantId;
    }
}
