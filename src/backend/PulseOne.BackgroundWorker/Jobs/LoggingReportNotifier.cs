using Microsoft.Extensions.Logging;
using PulseOne.SharedKernel.BackgroundJobs;

namespace PulseOne.BackgroundWorker.Jobs;

/// <summary>
/// Worker-side <see cref="IReportNotifier"/>. The SignalR <c>ReportHub</c> lives on the producer
/// (it owns the persistent client connections); the consumer cannot push to those connections
/// in-process.
/// </summary>
/// <remarks>
/// DEVIATION: in production the producer and consumer share an Azure SignalR Service backplane, and
/// this seam is swapped for one that sends through that backplane (or a service-bus relay) so a
/// worker can reach a tenant's group. Until that infra is provisioned (Phase 8 deployment), the
/// worker logs the notification — the report status is already persisted to the shard, so a polling
/// client still observes completion. Addressing remains per-tenant, so no cross-tenant leak is
/// possible regardless of transport.
/// </remarks>
public sealed class LoggingReportNotifier(ILogger<LoggingReportNotifier> log) : IReportNotifier
{
    public Task NotifyAsync(string tenantId, ReportNotification notification, CancellationToken ct = default)
    {
        log.LogInformation(
            "Report notification for tenant {TenantId}: report {ReportId} -> {Status}.",
            tenantId, notification.ReportId, notification.Status);
        return Task.CompletedTask;
    }
}
