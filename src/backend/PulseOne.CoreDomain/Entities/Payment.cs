using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.CoreDomain.Entities;

/// <summary>
/// A captured Razorpay payment recorded by the webhook worker (blueprint §6.3, Module 5). One row per
/// Razorpay payment id — the unique index plus the webhook event-id dedup together guarantee a
/// captured payment is recorded at most once even under Razorpay's at-least-once delivery.
/// </summary>
/// <remarks>
/// Multi-tenant + soft-deletable so the named filters compose and writes are audited. Amount is stored
/// in the smallest currency unit (paise for INR) exactly as Razorpay reports it, to avoid any
/// floating-point drift on money.
/// </remarks>
public sealed class Payment : BaseEntity, IMultiTenantEntity, ISoftDeletable
{
    /// <summary>The Razorpay payment id (<c>pay_…</c>). Unique per tenant; the applier upserts on it.</summary>
    public string RazorpayPaymentId { get; set; } = "";

    /// <summary>The Razorpay order id (<c>order_…</c>) this payment settled, if any.</summary>
    public string? RazorpayOrderId { get; set; }

    /// <summary>Amount in the smallest currency unit (paise for INR) — integer money, no float drift.</summary>
    public long AmountInPaise { get; set; }

    /// <summary>ISO currency code, e.g. "INR".</summary>
    public string Currency { get; set; } = "INR";

    /// <summary>Razorpay payment status, e.g. "captured".</summary>
    public string Status { get; set; } = "";

    /// <summary>UTC instant the payment was recorded by the applier.</summary>
    public DateTimeOffset CapturedAt { get; set; }

    // ISoftDeletable
    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    // IMultiTenantEntity
    public string TenantId { get; set; } = "";
}
