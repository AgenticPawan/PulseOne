using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.CoreDomain.Entities;

/// <summary>
/// A pending team invitation (Phase 6, Team module). Created when an operator invites an email
/// address to a role; the invitation email is sent by a background job (never inline). The invitee
/// has no IdP subject id yet, so an invitation is keyed by its own id and surfaces in the team list
/// as an "Invited" member until it is accepted or revoked.
/// </summary>
public sealed class TeamInvitation : BaseEntity, IMultiTenantEntity, ISoftDeletable
{
    public string Email { get; set; } = "";

    /// <summary>The role name the invitee will be granted on acceptance.</summary>
    public string Role { get; set; } = "";

    /// <summary>Opaque single-use acceptance token carried in the invitation email link.</summary>
    public string Token { get; set; } = "";

    /// <summary>"Pending", "Accepted", or "Revoked".</summary>
    public string Status { get; set; } = "Pending";

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public string TenantId { get; set; } = "";
}
