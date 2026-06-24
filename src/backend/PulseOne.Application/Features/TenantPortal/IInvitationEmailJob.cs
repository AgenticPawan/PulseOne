namespace PulseOne.Application.Features.TenantPortal;

/// <summary>
/// Producer-side seam for sending a team invitation email out-of-band (Phase 6 Team module —
/// "the invitation email is sent by a background job, never inline"). The team endpoint persists the
/// invitation then enqueues this contract; the worker resolves the concrete sender at execution time.
/// </summary>
public interface IInvitationEmailJob
{
    /// <summary>
    /// Sends the invitation for <paramref name="invitationId"/> to <paramref name="email"/> with an
    /// acceptance link built from <paramref name="token"/>, scoped to <paramref name="tenantId"/>.
    /// </summary>
    Task SendAsync(string tenantId, string invitationId, string email, string role, string token, CancellationToken ct);
}
