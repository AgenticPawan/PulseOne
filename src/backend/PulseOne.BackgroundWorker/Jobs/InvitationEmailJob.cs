using Hangfire;
using Microsoft.Extensions.Logging;
using PulseOne.Application.Features.TenantPortal;

namespace PulseOne.BackgroundWorker.Jobs;

/// <summary>
/// Worker-side <see cref="IInvitationEmailJob"/> (Phase 6 Team module). Enqueued by the tenant API
/// when an operator invites a user; Hangfire activates it from the worker DI scope and sends the
/// invitation email off the request thread (the API never sends mail inline).
/// </summary>
/// <remarks>
/// DEVIATION: no transactional email provider (e.g. Azure Communication Services / SendGrid) is
/// provisioned yet, so this logs the invitation instead of dispatching it — mirrors
/// <see cref="LoggingReportNotifier"/>. The invitation row is already persisted to the shard, so the
/// pending invite is visible in the team list regardless. Swap this seam for a real mail sender once
/// the provider is wired in (Phase 8 deployment).
/// </remarks>
public sealed class InvitationEmailJob(ILogger<InvitationEmailJob> log) : IInvitationEmailJob
{
    [Queue("default")]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 300])]
    public Task SendAsync(
        string tenantId, string invitationId, string email, string role, string token, CancellationToken ct)
    {
        // The token is the single-use acceptance secret — logged here only because no mail provider
        // exists yet; a real sender would embed it in the invitation link and never log it.
        log.LogInformation(
            "Team invitation {InvitationId} for tenant {TenantId}: invite {Email} as {Role}.",
            invitationId, tenantId, email, role);
        return Task.CompletedTask;
    }
}
