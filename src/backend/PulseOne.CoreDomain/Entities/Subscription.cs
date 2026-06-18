using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.CoreDomain.Entities;

/// <summary>
/// A tenant's Razorpay subscription, applied out-of-band by the webhook worker (blueprint §6.3,
/// Module 5). One row per Razorpay subscription id; mutated only by the idempotent applier on the
/// background worker, never inline on the webhook request thread (security rule #6).
/// </summary>
/// <remarks>
/// Multi-tenant + soft-deletable like <see cref="Report"/>, so the EF Core 10 "Tenant" and
/// "SoftDelete" named filters compose on it and its writes are audited by
/// <c>ApplicationDbContext.SaveChangesAsync</c>. The tenant id is stamped on insert from the resolved
/// tenant context — never accepted from the (untrusted) Razorpay payload directly into the filter.
/// </remarks>
public sealed class Subscription : BaseEntity, IMultiTenantEntity, ISoftDeletable
{
    /// <summary>The Razorpay subscription id (<c>sub_…</c>). Unique per tenant; the applier upserts on it.</summary>
    public string RazorpaySubscriptionId { get; set; } = "";

    /// <summary>The Razorpay plan id (<c>plan_…</c>) the subscription is on. Maps to a PulseOne tier.</summary>
    public string PlanId { get; set; } = "";

    /// <summary>Lifecycle mirror of the Razorpay subscription status: "active", "cancelled", etc.</summary>
    public string Status { get; set; } = "created";

    /// <summary>UTC instant the subscription was last activated (set on <c>subscription.activated</c>).</summary>
    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>UTC instant the subscription was cancelled (set on <c>subscription.cancelled</c>).</summary>
    public DateTimeOffset? CancelledAt { get; set; }

    // ISoftDeletable
    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    // IMultiTenantEntity
    public string TenantId { get; set; } = "";
}
