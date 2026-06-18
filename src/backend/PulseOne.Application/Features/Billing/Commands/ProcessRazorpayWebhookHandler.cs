using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;

namespace PulseOne.Application.Features.Billing.Commands;

/// <summary>
/// The terminal disposition of an inbound Razorpay webhook (blueprint §6.3). Drives the HTTP status
/// the endpoint returns: only <see cref="InvalidSignature"/> maps to 400; everything else acks 200 so
/// Razorpay stops retrying.
/// </summary>
public enum WebhookOutcome
{
    /// <summary>Signature valid, first delivery — work was enqueued. Ack 200.</summary>
    Verified,

    /// <summary>HMAC did not match — spoofed/corrupt delivery. Reject with 400 so it is NOT acked.</summary>
    InvalidSignature,

    /// <summary>Signature valid but the event id was already processed — ack 200, enqueue nothing.</summary>
    Duplicate,
}

/// <summary>
/// Command carrying a raw, unverified Razorpay webhook delivery into the pipeline (blueprint §6.3).
/// </summary>
/// <remarks>
/// This is a plain <see cref="IRequest{TResponse}"/>, NOT an <c>ICommand</c>: it deliberately does
/// NOT open a database transaction. Per security rule #6 (fast-ack pattern) the handler only verifies,
/// deduplicates, and ENQUEUES — the state mutation happens later on the background worker. Modeling it
/// as a non-transactional request keeps it off the tenant-bound DbContext path (the webhook is
/// anonymous and tenant-less at ingress).
/// <para>A <c>record</c> per CLAUDE.md ("record for commands/queries").</para>
/// </remarks>
public sealed record ProcessRazorpayWebhookCommand(string RawBody, string Signature, string EventId)
    : IRequest<WebhookOutcome>;

/// <summary>
/// Handles <see cref="ProcessRazorpayWebhookCommand"/> exactly as specified in blueprint §6.3, closing
/// three v1 defects (Appendix A #1/#2/#12): Key-Vault secret + constant-time verification, then
/// idempotency via Redis <c>SETNX</c>, then fast-ack enqueue instead of an inline mutation.
/// </summary>
public sealed class ProcessRazorpayWebhookHandler(
    IRazorpayWebhookVerifier verifier,
    IWebhookDeduplicationStore dedupe,           // Redis SETNX with TTL
    IBackgroundJobClient jobs,
    ILogger<ProcessRazorpayWebhookHandler> log)
    : IRequestHandler<ProcessRazorpayWebhookCommand, WebhookOutcome>
{
    public async Task<WebhookOutcome> Handle(
        ProcessRazorpayWebhookCommand request,
        CancellationToken cancellationToken)
    {
        // 1) Authenticity: constant-time HMAC check against the Key Vault-sourced secret. A failure is
        //    the ONLY case we 400 — authenticity comes from the signature, not a session.
        if (!verifier.IsValid(request.RawBody, request.Signature))
        {
            log.LogWarning("Razorpay webhook rejected: bad signature for event {EventId}.", request.EventId);
            return WebhookOutcome.InvalidSignature;
        }

        // 2) Idempotency: Razorpay retries deliver duplicates — apply each event exactly once. SETNX is
        //    atomic, so concurrent re-deliveries of the same id cannot both pass this gate.
        if (!await dedupe.TryMarkProcessedAsync(request.EventId, TimeSpan.FromDays(7), cancellationToken))
        {
            log.LogInformation("Razorpay event {EventId} already processed; acked, not re-applied.", request.EventId);
            return WebhookOutcome.Duplicate;
        }

        // 3) Fast-ack: hand the verified raw payload to a worker and return immediately. The mutation
        //    runs on PulseOne.BackgroundWorker, NEVER inline on this request thread (security rule #6).
        jobs.Enqueue<IRazorpaySubscriptionProcessor>(
            p => p.ApplyAsync(request.RawBody, request.EventId, CancellationToken.None));
        log.LogInformation("Razorpay event {EventId} verified and enqueued for processing.", request.EventId);
        return WebhookOutcome.Verified;
    }
}
