namespace PulseOne.SharedKernel.Domain;

/// <summary>
/// Base for all persisted business entities. Carries audit stamps written by the
/// DbContext's <c>SaveChangesAsync</c> override (blueprint §6.2).
/// </summary>
public abstract class BaseEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string CreatedBy { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
