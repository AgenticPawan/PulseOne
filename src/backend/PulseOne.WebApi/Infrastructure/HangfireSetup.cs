using Hangfire;
using Hangfire.SqlServer;

namespace PulseOne.WebApi.Infrastructure;

/// <summary>
/// Producer-side Hangfire wiring (blueprint §6, 01-hangfire-setup.md). The producer is the
/// stateless API: it ENQUEUES jobs onto the isolated Hangfire SQL backplane and returns fast.
/// It runs ZERO server workers — heavy compute belongs to <c>PulseOne.BackgroundWorker</c>
/// (the Azure Container Apps consumer scaled by the KEDA MSSQL trigger).
/// </summary>
public static class HangfireSetup
{
    /// <summary>
    /// Registers Hangfire job storage (Azure SQL) plus a worker-count-zero server. The
    /// zero-worker server is what lets the producer schedule recurring/enqueued jobs and expose
    /// <c>IBackgroundJobClient</c> without ever executing a job itself.
    /// </summary>
    public static IServiceCollection AddPulseOneHangfireProducer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Connection string is Key Vault-backed (ConnectionStrings:Hangfire). Never a literal.
        // <KEY_VAULT_REFERENCE>
        var hangfireConnection = configuration.GetConnectionString("Hangfire")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Hangfire is not configured. The producer cannot enqueue jobs " +
                "without the Hangfire backplane connection string (sourced from Key Vault).");

        services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(hangfireConnection, new SqlServerStorageOptions
            {
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.Zero,        // push-based dequeue; the consumer polls.
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true,                // required for SQL Azure throughput.
                // NOTE: PrepareSchemaIfNecessary defaults true — Hangfire self-provisions its job
                // tables here. PulseOne-authored tables (DeadLetterJobs) are migrated separately by
                // the MigrationRunner, never at app startup (CLAUDE.md).
            }));

        // Producer runs a server with ZERO workers: it can enqueue/schedule but never executes a
        // job. All execution happens on the consumer (01-hangfire-setup.md constraint).
        services.AddHangfireServer(o => o.WorkerCount = 0);

        return services;
    }
}
