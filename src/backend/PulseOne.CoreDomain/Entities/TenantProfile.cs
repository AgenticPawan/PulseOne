using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.CoreDomain.Entities;

/// <summary>
/// The tenant's company profile shown and edited under Settings (Phase 6). Exactly one row per
/// tenant — the service upserts it, so a tenant that has never saved settings reads back defaults.
/// </summary>
public sealed class TenantProfile : BaseEntity, IMultiTenantEntity
{
    public string CompanyName { get; set; } = "";

    public string ContactEmail { get; set; } = "";

    public string ContactPhone { get; set; } = "";

    /// <summary>Public URL of the uploaded logo, or null until one is set.</summary>
    public string? LogoUrl { get; set; }

    public string TenantId { get; set; } = "";
}
