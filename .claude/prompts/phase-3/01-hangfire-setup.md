# Prompt: Hangfire Background Job Infrastructure

## Context
PulseOne decouples request handling (producer) from heavy compute (consumer). Hangfire is the job backplane on an isolated Azure SQL DB. Workers run as Azure Container Apps scaled by KEDA MSSQL trigger.

## Task

### Producer Setup (`PulseOne.WebApi`)
```csharp
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("Hangfire"), new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true,
    }));
// Producer only enqueues — it does NOT start a HangfireServer
builder.Services.AddHangfireServer(o => o.WorkerCount = 0);  // zero workers on producer
```

### Consumer Setup (`PulseOne.BackgroundWorker`)
```csharp
builder.Services.AddHangfireServer(o =>
{
    o.WorkerCount     = int.Parse(Environment.GetEnvironmentVariable("HANGFIRE_WORKERS") ?? "5");
    o.Queues          = ["critical", "default", "bulk"];
    o.ServerTimeout   = TimeSpan.FromMinutes(30);
    o.HeartbeatInterval = TimeSpan.FromSeconds(15);
});
```

### Dead-Letter Queue (DLQ)
- After N retries (configurable, default 3), move failed jobs to `DeadLetterJob` table in Hangfire DB
- Send alert via Azure Monitor custom metric `hangfire.dlq.count`
- Implement `DeadLetterNotificationFilter : JobFilterAttribute` that fires `OnStateElection` when `FailedState` is final

```csharp
public sealed class DeadLetterNotificationFilter(ILogger<DeadLetterNotificationFilter> log) : JobFilterAttribute, IElectStateFilter
{
    public void OnStateElection(ElectStateContext ctx)
    {
        if (ctx.CandidateState is FailedState fs && ctx.CurrentState == FailedState.StateName)
        {
            log.LogError("Job {JobId} moved to DLQ after all retries: {Exception}", ctx.BackgroundJob.Id, fs.Exception.Message);
            // Emit OpenTelemetry counter: hangfire.dlq.count += 1
        }
    }
}
```

### W3C Trace Propagation Through Hangfire
Propagate `Activity` context via job args so consumer traces link to producer traces:
```csharp
public record TracedJobArgs(string TraceParent, string TraceState, string OriginalPayload);
// Enqueue: capture Activity.Current?.Id as TraceParent
// Consume: restore Activity using ActivityContext.Parse(args.TraceParent, ...)
```

### KEDA ScaledObject (`infra/keda-scaledobject.yaml`)
```yaml
apiVersion: keda.sh/v1alpha1
kind: ScaledObject
metadata:
  name: hangfire-worker-scaler
spec:
  scaleTargetRef:
    name: pulseone-worker
  minReplicaCount: 0          # scale-to-zero
  maxReplicaCount: 20
  triggers:
    - type: mssql
      metadata:
        connectionStringFromEnv: HANGFIRE_CONNECTION_STRING
        query: "SELECT COUNT(*) FROM HangFire.Job WHERE StateName='Enqueued'"
        targetValue: "10"     # 10 queued jobs per replica
```

## Output Locations
- `src/backend/PulseOne.WebApi/Infrastructure/HangfireSetup.cs`
- `src/backend/PulseOne.BackgroundWorker/Program.cs`
- `src/backend/PulseOne.BackgroundWorker/Jobs/DeadLetterNotificationFilter.cs`
- `infra/keda-scaledobject.yaml`

## Constraints
- Hangfire connection string comes from Key Vault — never in source
- Producer MUST have 0 server workers — it only enqueues
- DLQ alert must fire within 1 minute of a job exhausting retries (KEDA metric scrape interval)
- Trace context MUST be propagated — see §6.4 of blueprint for OpenTelemetry setup
