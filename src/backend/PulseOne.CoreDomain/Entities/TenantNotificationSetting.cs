using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.CoreDomain.Entities;

/// <summary>
/// A tenant's notification-channel preference for one event type (Phase 6, Settings). One row per
/// (tenant, event type); the set is seeded with sensible defaults the first time the tenant opens
/// notification settings, then persisted on every save.
/// </summary>
public sealed class TenantNotificationSetting : BaseEntity, IMultiTenantEntity
{
    /// <summary>Stable key for the event, e.g. "report.completed" or "billing.payment_failed".</summary>
    public string EventType { get; set; } = "";

    /// <summary>Human-friendly label shown in the settings grid.</summary>
    public string EventLabel { get; set; } = "";

    public bool Email { get; set; }

    public bool Sms { get; set; }

    public bool Whatsapp { get; set; }

    public string TenantId { get; set; } = "";
}
