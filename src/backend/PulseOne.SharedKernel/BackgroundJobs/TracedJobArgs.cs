using System.Diagnostics;

namespace PulseOne.SharedKernel.BackgroundJobs;

/// <summary>
/// Envelope that carries W3C trace context (blueprint §6.4: "W3C trace context propagated
/// through Hangfire job args") alongside the job payload, so a consumer span links back to the
/// producer request span and App Insights shows one correlated trace producer → queue → consumer.
/// </summary>
/// <remarks>
/// Hangfire serializes job arguments to its SQL store; an <c>Activity.Id</c> (the W3C
/// <c>traceparent</c>) and <c>Activity.TraceStateString</c> (the <c>tracestate</c>) survive that
/// round-trip as plain strings, which is why we capture them as the seam between hosts rather than
/// relying on an in-process <see cref="Activity"/> that does not cross the queue boundary.
/// <para>
/// This is a <c>record</c> per CLAUDE.md ("record for commands/queries"); job args are effectively
/// immutable command messages.
/// </para>
/// </remarks>
/// <param name="TraceParent">
/// The W3C <c>traceparent</c> header value (<see cref="Activity.Id"/> of the enqueuing span), or
/// <c>null</c> if no ambient activity was sampled at enqueue time.
/// </param>
/// <param name="TraceState">The W3C <c>tracestate</c> header value, or <c>null</c>.</param>
public sealed record TracedJobArgs(string? TraceParent, string? TraceState)
{
    /// <summary>
    /// Captures the current ambient <see cref="Activity"/> as a transportable trace context.
    /// Call this at <em>enqueue</em> time on the producer. Returns an empty envelope (no trace
    /// parent) when nothing is being traced, which is safe — the consumer simply starts a root span.
    /// </summary>
    public static TracedJobArgs Capture()
    {
        var current = Activity.Current;
        return new TracedJobArgs(current?.Id, current?.TraceStateString);
    }

    /// <summary>
    /// Restores the producer's <see cref="ActivityContext"/> from this envelope so a consumer span
    /// can be created as its child. Returns <c>false</c> when no valid trace parent was propagated.
    /// </summary>
    public bool TryGetParentContext(out ActivityContext context)
    {
        if (string.IsNullOrWhiteSpace(TraceParent))
        {
            context = default;
            return false;
        }

        // ActivityContext.TryParse validates the W3C traceparent format; a malformed value (e.g. a
        // truncated arg) must NOT crash the job — we fall back to a root span instead.
        return ActivityContext.TryParse(TraceParent, TraceState, out context);
    }
}
