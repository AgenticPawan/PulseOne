using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Options;
using PulseOne.Application.Features.Billing;

namespace PulseOne.Billing.Tests;

/// <summary>Minimal <see cref="IOptionsMonitor{TOptions}"/> returning a fixed value (no reload).</summary>
internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; } = value;
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>
/// Records every job creation so a test can assert the fast-ack handler enqueued EXACTLY ONCE (or
/// not at all on the duplicate/invalid paths). <c>IBackgroundJobClient.Enqueue&lt;T&gt;</c> is an
/// extension that funnels into <see cref="Create"/>, so counting Create calls is the reliable probe.
/// </summary>
internal sealed class RecordingBackgroundJobClient : IBackgroundJobClient
{
    public int CreateCount { get; private set; }

    public string Create(Job job, IState state)
    {
        CreateCount++;
        return Guid.NewGuid().ToString("n");
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}

/// <summary>In-memory dedup store: returns true the first time an id is seen, false thereafter.</summary>
internal sealed class InMemoryDeduplicationStore : IWebhookDeduplicationStore
{
    private readonly HashSet<string> _seen = [];

    public Task<bool> TryMarkProcessedAsync(string eventId, TimeSpan ttl, CancellationToken ct) =>
        Task.FromResult(_seen.Add(eventId));
}
