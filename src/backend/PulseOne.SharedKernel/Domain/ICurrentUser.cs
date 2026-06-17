namespace PulseOne.SharedKernel.Domain;

/// <summary>
/// The principal making the current request. Used for audit stamps and the
/// host-boundary authorization policy. Implemented in the WebApi layer over
/// the authenticated <c>ClaimsPrincipal</c>.
/// </summary>
public interface ICurrentUser
{
    string UserId { get; }
    string? TenantId { get; }
    bool IsHostOperator { get; }
}
