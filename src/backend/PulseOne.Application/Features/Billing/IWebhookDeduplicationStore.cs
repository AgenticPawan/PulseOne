namespace PulseOne.Application.Features.Billing;

/// <summary>
/// Idempotency gate for inbound webhooks (blueprint §6.3, Appendix A defect #12: "no idempotency on
/// webhook — Razorpay retries would double-apply payments"). Razorpay re-delivers the SAME event
/// (same <c>X-Razorpay-Event-Id</c>) until it gets a 200, so the handler must apply each event id at
/// most once.
/// </summary>
/// <remarks>
/// The Redis implementation uses an atomic <c>SETNX</c> (set-if-not-exists) with a 7-day TTL so the
/// "have I seen this event?" check and the "mark it seen" write are a SINGLE atomic operation — two
/// concurrent deliveries of the same event id cannot both win the race. The 7-day window comfortably
/// outlasts Razorpay's retry schedule while letting the key expire so the store does not grow without
/// bound.
/// </remarks>
public interface IWebhookDeduplicationStore
{
    /// <summary>
    /// Atomically records <paramref name="eventId"/> as processed. Returns <c>true</c> if THIS call
    /// was the first to claim the id (the caller should proceed to enqueue work), or <c>false</c> if
    /// the id was already present (a duplicate delivery — the caller must ack 200 but do nothing).
    /// </summary>
    /// <param name="eventId">The unique Razorpay event id from the <c>X-Razorpay-Event-Id</c> header.</param>
    /// <param name="ttl">How long the dedup marker lives (7 days per blueprint §6.3).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<bool> TryMarkProcessedAsync(string eventId, TimeSpan ttl, CancellationToken ct);
}
