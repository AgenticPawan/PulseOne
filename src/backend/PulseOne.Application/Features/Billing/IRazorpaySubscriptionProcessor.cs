namespace PulseOne.Application.Features.Billing;

/// <summary>
/// The out-of-band applier for a VERIFIED Razorpay webhook (blueprint §6.3). The producer API never
/// mutates state on the request thread (security rule #6 — fast-ack + queue); instead it enqueues a
/// call to this contract onto Hangfire and returns 200 immediately. The actual database mutation runs
/// on <c>PulseOne.BackgroundWorker</c> (the KEDA-scaled consumer) where it can take its time, retry,
/// and dead-letter without holding Razorpay's HTTP connection open.
/// </summary>
/// <remarks>
/// This interface lives in the Application layer (not the worker) so the PRODUCER can express
/// <c>IBackgroundJobClient.Enqueue&lt;IRazorpaySubscriptionProcessor&gt;(...)</c> without taking a
/// reference on the consumer assembly — Hangfire resolves the concrete
/// <c>RazorpaySubscriptionProcessor</c> from the worker's DI container at execution time.
/// </remarks>
public interface IRazorpaySubscriptionProcessor
{
    /// <summary>
    /// Applies a single, already-signature-verified, already-deduplicated webhook event exactly once.
    /// </summary>
    /// <param name="rawBody">
    /// The raw JSON body Razorpay delivered. The body — not a re-serialized DTO — is carried across
    /// the queue so the consumer parses the authoritative payload itself.
    /// </param>
    /// <param name="eventId">The Razorpay event id (carried for correlation / structured logging).</param>
    /// <param name="ct">Cancellation token supplied by the Hangfire job activation scope.</param>
    Task ApplyAsync(string rawBody, string eventId, CancellationToken ct);
}
