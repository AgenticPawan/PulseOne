using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PulseOne.Application.Features.Billing;
using PulseOne.CoreDomain.Entities;
using PulseOne.Infrastructure.Persistence;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.BackgroundWorker.Jobs;

/// <summary>
/// Consumer-side applier for VERIFIED Razorpay webhook events (blueprint §6.3, prompt
/// 01-webhook-verifier.md). The producer fast-acked the HTTP request and enqueued a call to this job;
/// here — off the request thread, on the KEDA-scaled worker — we parse the authoritative raw payload
/// and apply the subscription/payment change EXACTLY ONCE.
/// </summary>
/// <remarks>
/// IDEMPOTENCY (defence in depth): the producer already suppressed duplicate <c>X-Razorpay-Event-Id</c>
/// deliveries via Redis SETNX, but Hangfire's own at-least-once execution (a retry after a transient
/// fault) could still re-run THIS job for the same event. So every apply path upserts on the Razorpay
/// entity id (unique per tenant) rather than blindly inserting — re-execution is a no-op, never a
/// double-charge (Appendix A #12).
/// <para>
/// TENANCY: the job NEVER inherits a request context (there is none on the worker). It reads the
/// PulseOne tenant id from the Razorpay <c>notes.tenant_id</c> we set at order/subscription creation,
/// resolves its OWN fail-closed <see cref="ITenantContext"/> from it, and builds a shard-bound
/// <see cref="ApplicationDbContext"/> — so the tenant query filter and audit writer are correctly
/// scoped (mirrors <see cref="ReportProcessorJob"/>). An absent tenant id throws before any write.
/// </para>
/// </remarks>
public sealed class RazorpaySubscriptionProcessor(
    ITenantContext tenantContext,
    IShardDbContextFactory shardFactory,
    ILogger<RazorpaySubscriptionProcessor> log) : IRazorpaySubscriptionProcessor
{
    private const string TenantNoteKey = "tenant_id";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Critical-queue job (subscription state gates feature access, so it jumps ahead of bulk/report
    /// work). Five attempts with Hangfire's default backoff; on terminal failure the global
    /// dead-letter filter captures it for operator follow-up.
    /// </summary>
    [Queue("critical")]
    [AutomaticRetry(Attempts = 5)]
    public async Task ApplyAsync(string rawBody, string eventId, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<RazorpayWebhookPayload>(rawBody, JsonOptions);
        if (payload?.Event is null)
        {
            // A verified-but-unparseable body is a contract drift, not a transient fault — do not
            // retry forever. Log and return so the job completes (Razorpay was already 200-acked).
            log.LogWarning("Razorpay event {EventId} had no recognizable 'event' field; skipped.", eventId);
            return;
        }

        log.LogInformation("Applying Razorpay event {EventId} of type {EventType}.", eventId, payload.Event);

        switch (payload.Event)
        {
            case "subscription.activated":
                await ActivateSubscriptionAsync(payload, eventId, ct);
                break;
            case "subscription.cancelled":
                await CancelSubscriptionAsync(payload, eventId, ct);
                break;
            case "payment.captured":
                await RecordPaymentAsync(payload, eventId, ct);
                break;
            default:
                // Unhandled but valid event type — ack and ignore (we only subscribe to a subset).
                log.LogInformation("Razorpay event type {EventType} is not handled; ignored.", payload.Event);
                break;
        }
    }

    private async Task ActivateSubscriptionAsync(RazorpayWebhookPayload payload, string eventId, CancellationToken ct)
    {
        var sub = payload.Payload?.Subscription?.Entity
            ?? throw new InvalidOperationException($"Event {eventId}: subscription.activated had no subscription entity.");

        var tenantId = ResolveTenant(sub.Notes, eventId);
        await using var db = await BuildContextAsync(tenantId, ct);

        // Upsert on the Razorpay subscription id so a Hangfire re-run is a no-op (idempotent).
        var existing = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.RazorpaySubscriptionId == sub.Id, ct);

        if (existing is null)
        {
            db.Subscriptions.Add(new Subscription
            {
                RazorpaySubscriptionId = sub.Id ?? "",
                PlanId = sub.PlanId ?? "",
                Status = sub.Status ?? "active",
                ActivatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.Status = sub.Status ?? "active";
            existing.PlanId = sub.PlanId ?? existing.PlanId;
            existing.ActivatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Subscription {SubId} activated for tenant {TenantId}.", sub.Id, tenantId);
    }

    private async Task CancelSubscriptionAsync(RazorpayWebhookPayload payload, string eventId, CancellationToken ct)
    {
        var sub = payload.Payload?.Subscription?.Entity
            ?? throw new InvalidOperationException($"Event {eventId}: subscription.cancelled had no subscription entity.");

        var tenantId = ResolveTenant(sub.Notes, eventId);
        await using var db = await BuildContextAsync(tenantId, ct);

        var existing = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.RazorpaySubscriptionId == sub.Id, ct);

        if (existing is null)
        {
            // A cancel for a subscription we never activated — record it so state is consistent.
            db.Subscriptions.Add(new Subscription
            {
                RazorpaySubscriptionId = sub.Id ?? "",
                PlanId = sub.PlanId ?? "",
                Status = "cancelled",
                CancelledAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.Status = "cancelled";
            existing.CancelledAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Subscription {SubId} cancelled for tenant {TenantId}.", sub.Id, tenantId);
    }

    private async Task RecordPaymentAsync(RazorpayWebhookPayload payload, string eventId, CancellationToken ct)
    {
        var pay = payload.Payload?.Payment?.Entity
            ?? throw new InvalidOperationException($"Event {eventId}: payment.captured had no payment entity.");

        var tenantId = ResolveTenant(pay.Notes, eventId);
        await using var db = await BuildContextAsync(tenantId, ct);

        // Idempotent upsert keyed on the Razorpay payment id — a re-run never double-records money.
        var exists = await db.Payments.AnyAsync(p => p.RazorpayPaymentId == pay.Id, ct);
        if (exists)
        {
            log.LogInformation("Payment {PayId} already recorded for tenant {TenantId}; skipped.", pay.Id, tenantId);
            return;
        }

        db.Payments.Add(new Payment
        {
            RazorpayPaymentId = pay.Id ?? "",
            RazorpayOrderId = pay.OrderId,
            AmountInPaise = pay.Amount,
            Currency = pay.Currency ?? "INR",
            Status = pay.Status ?? "captured",
            CapturedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        log.LogInformation("Payment {PayId} ({Amount} {Currency}) recorded for tenant {TenantId}.",
            pay.Id, pay.Amount, pay.Currency, tenantId);
    }

    /// <summary>
    /// Resolves the PulseOne tenant id from Razorpay <c>notes</c> and binds the fail-closed tenant
    /// context. Throws (no default) if the note is missing — fail-closed on tenancy (security rule #5).
    /// </summary>
    private string ResolveTenant(Dictionary<string, string>? notes, string eventId)
    {
        if (notes is null || !notes.TryGetValue(TenantNoteKey, out var tenantId) || string.IsNullOrWhiteSpace(tenantId))
        {
            throw new TenantResolutionException(
                $"Razorpay event {eventId} carried no '{TenantNoteKey}' note; cannot resolve tenant. Rejected (fail-closed).");
        }

        // Resolve THIS job's tenant from the payload note — never an ambient request context.
        tenantContext.Resolve(tenantId);
        return tenantId;
    }

    private Task<ApplicationDbContext> BuildContextAsync(string tenantId, CancellationToken ct) =>
        shardFactory.CreateAsync(tenantId, ct);
}
