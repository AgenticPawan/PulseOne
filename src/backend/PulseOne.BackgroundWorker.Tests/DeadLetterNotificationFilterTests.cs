using System.Diagnostics.Metrics;
using Hangfire;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using PulseOne.BackgroundWorker.Jobs;
using PulseOne.SharedKernel.BackgroundJobs;
using PulseOne.SharedKernel.Logging;
using Xunit;

namespace PulseOne.BackgroundWorker.Tests;

/// <summary>
/// Charter DLQ verification: a job that exhausts its retries must (1) fire the
/// <see cref="DeadLetterNotificationFilter"/>, which (2) persists a dead-letter record and
/// (3) increments the <c>hangfire.dlq.count</c> OpenTelemetry counter.
/// </summary>
public sealed class DeadLetterNotificationFilterTests
{
    // A job whose only purpose is to always throw — the canonical poisoned job.
    public sealed class AlwaysThrowsJob
    {
        [AutomaticRetry(Attempts = 1)] // matches the charter's test configuration
        public void Run() => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void OnStateElection_WhenFailedStateIsTerminal_RecordsDeadLetterAndIncrementsCounter()
    {
        // --- Arrange: capture the hangfire.dlq.count metric via a MeterListener -------------------
        long dlqCount = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == Telemetry.ServiceName && instrument.Name == "hangfire.dlq.count")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => dlqCount += measurement);
        listener.Start();

        var store = new RecordingDeadLetterStore();
        var sut = new DeadLetterNotificationFilter(store, NullLogger<DeadLetterNotificationFilter>.Instance);

        var context = BuildElectStateContext(
            job: Job.FromExpression<AlwaysThrowsJob>(j => j.Run()),
            candidate: new FailedState(new InvalidOperationException("boom")));

        // --- Act ---------------------------------------------------------------------------------
        sut.OnStateElection(context);
        listener.RecordObservableInstruments();

        // --- Assert ------------------------------------------------------------------------------
        Assert.Equal(1, dlqCount);                     // counter incremented exactly once
        Assert.Single(store.Records);                  // store received exactly one record
        Assert.Equal("boom", store.Records[0].ExceptionMessage);
        Assert.Contains(nameof(AlwaysThrowsJob), store.Records[0].JobType);
    }

    [Fact]
    public void OnStateElection_WhenCandidateIsNotFailed_DoesNothing()
    {
        var store = new RecordingDeadLetterStore();
        var sut = new DeadLetterNotificationFilter(store, NullLogger<DeadLetterNotificationFilter>.Instance);

        var context = BuildElectStateContext(
            job: Job.FromExpression<AlwaysThrowsJob>(j => j.Run()),
            // A reschedule (retry still pending) must NOT be dead-lettered.
            candidate: new ScheduledState(TimeSpan.FromSeconds(30)));

        sut.OnStateElection(context);

        Assert.Empty(store.Records);
    }

    private static ElectStateContext BuildElectStateContext(Job job, IState candidate)
    {
        var backgroundJob = new BackgroundJob("job-1", job, DateTime.UtcNow);
        var storage = new FakeStorage();
        var connection = new FakeConnection();
        var transaction = new FakeTransaction();

        var apply = new ApplyStateContext(
            storage, connection, transaction, backgroundJob, candidate, oldStateName: ProcessingState.StateName);

        return new ElectStateContext(apply);
    }

    private sealed class RecordingDeadLetterStore : IDeadLetterStore
    {
        public List<DeadLetterRecord> Records { get; } = [];

        public Task RecordAsync(DeadLetterRecord record, CancellationToken ct = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    // Minimal Hangfire storage fakes: implement only what the filter's code path touches; every
    // other member throws so an accidental dependency surfaces loudly rather than silently passing.
    private sealed class FakeStorage : JobStorage
    {
        public override IMonitoringApi GetMonitoringApi() => throw new NotSupportedException();
        public override IStorageConnection GetConnection() => new FakeConnection();
    }

    private sealed class FakeConnection : JobStorageConnection
    {
        // The filter reads the "CurrentQueue" job parameter; return null so it falls back to default.
        public override string? GetJobParameter(string id, string name) => null;

        public override IWriteOnlyTransaction CreateWriteTransaction() => new FakeTransaction();
        public override IDisposable AcquireDistributedLock(string resource, TimeSpan timeout) => throw new NotSupportedException();
        public override string CreateExpiredJob(Job job, IDictionary<string, string> parameters, DateTime createdAt, TimeSpan expireIn) => throw new NotSupportedException();
        public override IFetchedJob FetchNextJob(string[] queues, CancellationToken cancellationToken) => throw new NotSupportedException();
        public override void SetJobParameter(string id, string name, string value) => throw new NotSupportedException();
        public override JobData GetJobData(string jobId) => throw new NotSupportedException();
        public override StateData GetStateData(string jobId) => throw new NotSupportedException();
        public override void AnnounceServer(string serverId, ServerContext context) => throw new NotSupportedException();
        public override void RemoveServer(string serverId) => throw new NotSupportedException();
        public override void Heartbeat(string serverId) => throw new NotSupportedException();
        public override int RemoveTimedOutServers(TimeSpan timeOut) => throw new NotSupportedException();
        public override HashSet<string> GetAllItemsFromSet(string key) => throw new NotSupportedException();
        public override string? GetFirstByLowestScoreFromSet(string key, double fromScore, double toScore) => throw new NotSupportedException();
        public override void SetRangeInHash(string key, IEnumerable<KeyValuePair<string, string>> keyValuePairs) => throw new NotSupportedException();
        public override Dictionary<string, string> GetAllEntriesFromHash(string key) => throw new NotSupportedException();
    }

    private sealed class FakeTransaction : IWriteOnlyTransaction
    {
        public void Dispose() { }
        public void ExpireJob(string jobId, TimeSpan expireIn) { }
        public void PersistJob(string jobId) { }
        public void SetJobState(string jobId, IState state) { }
        public void AddJobState(string jobId, IState state) { }
        public void AddToQueue(string queue, string jobId) { }
        public void IncrementCounter(string key) { }
        public void IncrementCounter(string key, TimeSpan expireIn) { }
        public void DecrementCounter(string key) { }
        public void DecrementCounter(string key, TimeSpan expireIn) { }
        public void AddToSet(string key, string value) { }
        public void AddToSet(string key, string value, double score) { }
        public void RemoveFromSet(string key, string value) { }
        public void InsertToList(string key, string value) { }
        public void RemoveFromList(string key, string value) { }
        public void TrimList(string key, int keepStartingFrom, int keepEndingAt) { }
        public void SetRangeInHash(string key, IEnumerable<KeyValuePair<string, string>> keyValuePairs) { }
        public void RemoveHash(string key) { }
        public void Commit() { }
    }
}
