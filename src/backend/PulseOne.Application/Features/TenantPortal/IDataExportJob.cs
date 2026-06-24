namespace PulseOne.Application.Features.TenantPortal;

/// <summary>
/// Producer-side seam for a tenant's full-data export (Phase 6 Settings danger zone). The settings
/// endpoint enqueues this contract and returns the Hangfire job id immediately; the worker compiles
/// the tenant's data into an artifact and uploads it to the tenant's blob container off-request.
/// </summary>
public interface IDataExportJob
{
    /// <summary>Exports all data for <paramref name="tenantId"/>, requested by <paramref name="requestedByUserId"/>.</summary>
    Task ExportAsync(string tenantId, string requestedByUserId, CancellationToken ct);
}
