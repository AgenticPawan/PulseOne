using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PulseOne.SharedKernel.MultiTenancy;
using PulseOne.SharedKernel.Security;

namespace PulseOne.WebApi.Hubs;

/// <summary>
/// SignalR hub that streams report-completion notifications to tenants (02-report-worker.md).
/// Lives on the producer (the API serves the persistent connections). On connect, a client is
/// joined to the SignalR group named after its OWN tenant id, derived from the validated
/// <c>tenant_id</c> claim — never a client-supplied value — so a connection can only ever receive
/// its own tenant's notifications.
/// </summary>
[Authorize]
public sealed class ReportHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User?.FindFirst(AuthClaimTypes.TenantId)?.Value;

        // Fail-closed: a tenant-scoped connection with no resolved tenant is aborted rather than
        // silently joining no group (mirrors ITenantContext's fail-closed contract).
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            Context.Abort();
            throw new TenantResolutionException(
                "Report hub connection rejected: the principal carries no tenant_id claim.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tenantId));
        await base.OnConnectedAsync();
    }

    /// <summary>The SignalR group a tenant's clients share. Centralized so the notifier and the
    /// hub agree on the naming convention.</summary>
    public static string GroupName(string tenantId) => $"tenant:{tenantId}";
}

/// <summary>
/// Producer-side <see cref="IReportNotifier"/> over the SignalR <see cref="ReportHub"/>. The
/// background worker resolves this seam to push completion events; addressing is by tenant group
/// only, so a worker cannot target another tenant's connections.
/// </summary>
public sealed class SignalRReportNotifier(IHubContext<ReportHub> hub) : SharedKernel.BackgroundJobs.IReportNotifier
{
    public Task NotifyAsync(
        string tenantId,
        SharedKernel.BackgroundJobs.ReportNotification notification,
        CancellationToken ct = default) =>
        hub.Clients
            .Group(ReportHub.GroupName(tenantId))
            .SendAsync("ReportCompleted", notification, ct);
}
