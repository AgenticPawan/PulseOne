using System.Text.Json.Serialization;

namespace PulseOne.Application.Features.Billing;

/// <summary>
/// Minimal projection of a Razorpay webhook envelope (blueprint §6.3). Only the fields the
/// subscription applier needs are modeled — Razorpay's envelope is large and version-dependent, so we
/// bind defensively (everything nullable) and switch on <see cref="Event"/>.
/// </summary>
/// <remarks>
/// TENANCY: PulseOne links a Razorpay subscription/payment back to a PulseOne tenant via Razorpay
/// <c>notes</c> (key/value metadata we set when CREATING the order/subscription). The applier reads
/// <c>notes["tenant_id"]</c> and resolves its own fail-closed tenant scope from it — it never trusts
/// an ambient request context (there is none on the worker) and never defaults the tenant.
/// </remarks>
public sealed record RazorpayWebhookPayload
{
    /// <summary>The event name, e.g. <c>"subscription.activated"</c>, <c>"payment.captured"</c>.</summary>
    [JsonPropertyName("event")]
    public string? Event { get; init; }

    /// <summary>The event payload container (entities keyed by type: <c>subscription</c>, <c>payment</c>).</summary>
    [JsonPropertyName("payload")]
    public RazorpayPayloadContainer? Payload { get; init; }
}

/// <summary>Container for the typed entities a webhook event carries.</summary>
public sealed record RazorpayPayloadContainer
{
    [JsonPropertyName("subscription")]
    public RazorpayEntityWrapper<RazorpaySubscriptionEntity>? Subscription { get; init; }

    [JsonPropertyName("payment")]
    public RazorpayEntityWrapper<RazorpayPaymentEntity>? Payment { get; init; }
}

/// <summary>Razorpay nests each entity under an <c>"entity"</c> property; this unwraps that shape.</summary>
/// <typeparam name="TEntity">The concrete entity type (subscription/payment).</typeparam>
public sealed record RazorpayEntityWrapper<TEntity>
{
    [JsonPropertyName("entity")]
    public TEntity? Entity { get; init; }
}

/// <summary>The Razorpay subscription entity (subset used by the applier).</summary>
public sealed record RazorpaySubscriptionEntity
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("plan_id")]
    public string? PlanId { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>Free-form metadata we set at creation time — carries <c>tenant_id</c>.</summary>
    [JsonPropertyName("notes")]
    public Dictionary<string, string>? Notes { get; init; }
}

/// <summary>The Razorpay payment entity (subset used by the applier).</summary>
public sealed record RazorpayPaymentEntity
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("order_id")]
    public string? OrderId { get; init; }

    /// <summary>Amount in the smallest currency unit (paise for INR).</summary>
    [JsonPropertyName("amount")]
    public long Amount { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("notes")]
    public Dictionary<string, string>? Notes { get; init; }
}
