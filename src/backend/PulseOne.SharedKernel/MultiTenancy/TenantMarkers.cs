namespace PulseOne.SharedKernel.MultiTenancy;

/// <summary>
/// Marker for entities that are partitioned by tenant. The EF Core "Tenant" named
/// query filter is applied to every type implementing this interface (blueprint §6.2).
/// </summary>
public interface IMultiTenantEntity
{
    string TenantId { get; set; }
}

/// <summary>
/// Marker for entities that support soft delete. The EF Core "SoftDelete" named query
/// filter is applied to every type implementing this interface; the two filters compose
/// via EF Core 10 named filters (blueprint §6.2).
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
}
