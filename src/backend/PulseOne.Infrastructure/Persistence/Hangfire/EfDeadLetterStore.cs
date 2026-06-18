using Microsoft.Extensions.Logging;
using PulseOne.SharedKernel.BackgroundJobs;

namespace PulseOne.Infrastructure.Persistence.Hangfire;

/// <summary>
/// <see cref="IDeadLetterStore"/> backed by the isolated Hangfire DB. Persists one
/// <see cref="DeadLetterJob"/> row per job that exhausts all retries.
/// </summary>
/// <remarks>
/// Constructed with the Hangfire connection string (Key Vault-backed; never literal) because the
/// caller — <c>DeadLetterNotificationFilter.OnStateElection</c> — runs in Hangfire's pipeline with
/// no request scope. The write is best-effort: a persistence failure is logged but swallowed so it
/// can never block the job from reaching <c>FailedState</c> (which would corrupt Hangfire's state
/// machine). The OpenTelemetry counter is emitted by the filter regardless of this write.
/// </remarks>
public sealed class EfDeadLetterStore(string connectionString, ILogger<EfDeadLetterStore> log)
    : IDeadLetterStore
{
    public async Task RecordAsync(DeadLetterRecord record, CancellationToken ct = default)
    {
        try
        {
            await using var db = HangfireDbContextFactory.Create(connectionString);
            db.DeadLetterJobs.Add(new DeadLetterJob
            {
                JobId = record.JobId,
                JobType = record.JobType,
                Queue = record.Queue,
                TenantId = record.TenantId,
                ExceptionType = record.ExceptionType,
                ExceptionMessage = record.ExceptionMessage,
                ExceptionDetail = record.ExceptionDetail,
                FailedAt = record.FailedAt,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Never rethrow into Hangfire's state-election pipeline. The alert metric still fires.
            log.LogError(ex,
                "Failed to persist dead-letter record for job {JobId}. The DLQ metric still fired.",
                record.JobId);
        }
    }
}
