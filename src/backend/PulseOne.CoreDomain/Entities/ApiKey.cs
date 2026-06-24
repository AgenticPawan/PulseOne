using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.CoreDomain.Entities;

/// <summary>
/// A tenant-scoped programmatic API key (Phase 6, Settings). The plaintext secret is shown to the
/// caller exactly ONCE at creation and is never persisted: only a SHA-256 <see cref="SecretHash"/>
/// and a short, non-secret <see cref="Prefix"/> (for display/identification) are stored. Revocation
/// is a soft-delete so the audit trail of who held a key is preserved.
/// </summary>
public sealed class ApiKey : BaseEntity, IMultiTenantEntity, ISoftDeletable
{
    public string Name { get; set; } = "";

    /// <summary>The first few characters of the key, shown in listings so a user can tell keys apart.</summary>
    public string Prefix { get; set; } = "";

    /// <summary>Lowercase hex SHA-256 of the full secret. The plaintext is never stored.</summary>
    public string SecretHash { get; set; } = "";

    public DateTimeOffset? LastUsedUtc { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public string TenantId { get; set; } = "";
}
