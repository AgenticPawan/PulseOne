namespace PulseOne.Infrastructure.Persistence.Catalog;

/// <summary>Subscription tier — drives shard placement and feature gates.</summary>
public enum TenantTier
{
    Free = 0,
    Pro = 1,
    Enterprise = 2
}

/// <summary>
/// Operational lifecycle of a tenant as managed from the host admin portal (blueprint §6,
/// Module 1). Distinct from <see cref="TenantShard.IsActive"/>, which is the routing flag the
/// tenant-resolution pipeline reads: only <see cref="Active"/> tenants route to their shard.
/// </summary>
public enum TenantStatus
{
    Provisioning = 0,
    Active = 1,
    Suspended = 2,
    Decommissioned = 3
}

/// <summary>
/// Tenant Catalog row: maps a tenant id to its business shard. This is infrastructure
/// data, not business data — no soft-delete and no audit stamps (blueprint §1, §6.1).
/// The host admin portal (Module 1) is the sole writer of these rows.
/// </summary>
public sealed class TenantShard
{
    public string TenantId { get; set; } = default!;

    /// <summary>Display/company name shown in the host portal tenant list.</summary>
    public string Name { get; set; } = default!;

    /// <summary>Primary administrator email captured at provisioning.</summary>
    public string AdminEmail { get; set; } = default!;

    // DEVIATION: connection string stored in catalog DB, not Key Vault, per the
    // prompt's explicit data model (TenantShard.ShardConnectionString). The catalog
    // DB itself is protected and its own connection string IS a Key Vault reference.
    public string ShardConnectionString { get; set; } = default!;

    /// <summary>Operator-facing shard label (e.g. "Shard01") assigned at provisioning.</summary>
    public string ShardLabel { get; set; } = default!;

    public string Region { get; set; } = default!;

    public TenantTier Tier { get; set; } = TenantTier.Free;

    /// <summary>Host-portal lifecycle state. Kept in sync with <see cref="IsActive"/>.</summary>
    public TenantStatus Status { get; set; } = TenantStatus.Active;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Routing flag read by the tenant-resolution pipeline. True only when <see cref="Status"/> is Active.</summary>
    public bool IsActive { get; set; } = true;
}
