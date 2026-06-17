namespace PulseOne.SharedKernel.MultiTenancy;

/// <summary>
/// Maps a tenant id to its shard. Backed by the Tenant Catalog DB (see blueprint §1, §6.1).
/// Implementations live in the Infrastructure layer; SharedKernel only defines the contract.
/// </summary>
public interface ITenantCatalog
{
    /// <summary>
    /// Returns true if the tenant exists and is active. MUST return false for unknown
    /// tenants — never throws (constraint 02-tenant-catalog.md).
    /// </summary>
    Task<bool> ExistsAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Returns the shard connection string for the tenant, or null if the tenant is unknown.
    /// </summary>
    Task<string?> GetConnectionStringAsync(string tenantId, CancellationToken ct = default);
}
