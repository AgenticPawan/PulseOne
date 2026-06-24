using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.CoreDomain.Entities;

/// <summary>
/// Per-tenant overlay for a team member's portal-managed state (Phase 6, Team module). Role grants
/// themselves live in <c>TenantUserRole</c>; this row carries the bits the host IdP does NOT mirror
/// into the shard — the operator-set activation status and any locally-cached profile fields.
/// </summary>
/// <remarks>
/// A row is created on demand the first time a member is deactivated (or when an invite is accepted),
/// so an active member with default state may have no row at all — the team list defaults such users
/// to <c>Active</c>. <see cref="Email"/>/<see cref="DisplayName"/>/<see cref="LastLoginUtc"/> are
/// best-effort mirrors of IdP fields; until an IdP sync is wired in they fall back to the user id.
/// </remarks>
public sealed class TenantMember : BaseEntity, IMultiTenantEntity, ISoftDeletable
{
    /// <summary>The IdP subject id this membership state belongs to (unique within the tenant).</summary>
    public string UserId { get; set; } = "";

    public string Email { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>"Active" or "Deactivated" — the operator-controlled access flag for real members.</summary>
    public string Status { get; set; } = "Active";

    public DateTimeOffset? LastLoginUtc { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public string TenantId { get; set; } = "";
}
