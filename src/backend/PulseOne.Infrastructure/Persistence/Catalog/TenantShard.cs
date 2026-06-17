namespace PulseOne.Infrastructure.Persistence.Catalog;

/// <summary>Subscription tier — drives shard placement and feature gates.</summary>
public enum TenantTier
{
    Free = 0,
    Pro = 1,
    Enterprise = 2
}

/// <summary>
/// Tenant Catalog row: maps a tenant id to its business shard. This is infrastructure
/// data, not business data — no soft-delete and no audit stamps (blueprint §1, §6.1).
/// </summary>
public sealed class TenantShard
{
    public string TenantId { get; set; } = default!;

    // DEVIATION: connection string stored in catalog DB, not Key Vault, per the
    // prompt's explicit data model (TenantShard.ShardConnectionString). The catalog
    // DB itself is protected and its own connection string IS a Key Vault reference.
    public string ShardConnectionString { get; set; } = default!;

    public string Region { get; set; } = default!;

    public TenantTier Tier { get; set; } = TenantTier.Free;

    public DateTimeOffset CreatedAt { get; set; }

    public bool IsActive { get; set; } = true;
}
