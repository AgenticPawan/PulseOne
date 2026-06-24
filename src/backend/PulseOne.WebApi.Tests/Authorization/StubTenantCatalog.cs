using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.WebApi.Tests.Authorization;

/// <summary>
/// Test <see cref="ITenantCatalog"/> that treats every tenant as existing. The tenant-portal routes
/// run through <c>TenantResolutionMiddleware</c>, which rejects the request with 400 unless the
/// catalog confirms the tenant — so the PBAC allow-path tests need a catalog that resolves without a
/// real Redis/SQL backend. Isolation is proven separately at the service layer; here the catalog only
/// has to let a well-formed tenant request reach the authorization stage.
/// </summary>
public sealed class StubTenantCatalog : ITenantCatalog
{
    public Task<bool> ExistsAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(tenantId));

    public Task<string?> GetConnectionStringAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task InvalidateAsync(string tenantId, CancellationToken ct = default) => Task.CompletedTask;
}
