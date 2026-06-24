using Microsoft.EntityFrameworkCore;
using PulseOne.CoreDomain.Entities;
using Xunit;

namespace PulseOne.Infrastructure.Tests.Isolation;

/// <summary>
/// The host's SANCTIONED cross-tenant read path (blueprint §6, Modules 2/3). <c>HostAdminService</c>
/// builds an <see cref="Persistence.ApplicationDbContext"/> over a pre-resolved (non-throwing) tenant
/// context and calls <c>IgnoreQueryFilters(["Tenant"])</c> to read across tenants. This suite proves
/// that bypass is SURGICAL: it lifts ONLY the "Tenant" named filter and the "SoftDelete" filter STILL
/// applies, so a host operator never sees soft-deleted rows by accident.
/// </summary>
/// <remarks>
/// <c>HostAdminService.BuildContext</c> hardcodes <c>UseSqlServer</c>, so these tests exercise the
/// same DbContext behaviour directly over SQLite (the security-critical part is the filter bypass,
/// which is provider-agnostic). The host's real <c>FixedTenantContext</c> is internal to
/// Infrastructure; <see cref="TenantIsolationTestBase.PreResolvedTenantContext"/> is its faithful,
/// non-throwing stand-in.
/// </remarks>
[Trait("Category", "Isolation")]
public sealed class HostCrossTenantPathTests : TenantIsolationTestBase
{
    [Fact]
    public async Task IgnoreTenantFilter_returns_rows_from_every_tenant()
    {
        await SeedReportsAsync("A", rowCount: 3);
        await SeedReportsAsync("B", rowCount: 5);

        // Host-scoped context carries a sentinel tenant that is never evaluated (the Tenant filter is
        // bypassed). This mirrors HostAdminService's cross-tenant queries.
        await using var host = NewContextWith(new PreResolvedTenantContext("__host__"));
        var all = await host.Reports.IgnoreQueryFilters(["Tenant"]).ToListAsync();

        Assert.Equal(8, all.Count);
        Assert.Contains(all, r => r.TenantId == "A");
        Assert.Contains(all, r => r.TenantId == "B");
    }

    [Fact]
    public async Task IgnoreTenantFilter_still_applies_SoftDelete_filter()
    {
        await SeedReportsAsync("A", rowCount: 2);
        await SeedReportsAsync("B", rowCount: 2);

        // Soft-delete one of A's rows.
        await using (var ctx = NewContextFor("A"))
        {
            var first = await ctx.Reports.FirstAsync();
            ctx.Reports.Remove(first);
            await ctx.SaveChangesAsync();
        }

        await using var host = NewContextWith(new PreResolvedTenantContext("__host__"));

        // Bypassing ONLY the Tenant filter: cross-tenant rows are visible, but the soft-deleted row is
        // NOT — the SoftDelete named filter remains in force.
        var crossTenant = await host.Reports.IgnoreQueryFilters(["Tenant"]).ToListAsync();
        Assert.Equal(3, crossTenant.Count); // 4 seeded - 1 soft-deleted
        Assert.All(crossTenant, r => Assert.False(r.IsDeleted));
    }

    [Fact]
    public async Task Bound_host_context_without_ignore_is_scoped_to_its_tenant()
    {
        // When the host binds the context to a specific tenant (e.g. per-tenant audit) and does NOT
        // call IgnoreQueryFilters, the Tenant filter is in force and scopes to that tenant only.
        await SeedReportsAsync("A", rowCount: 3);
        await SeedReportsAsync("B", rowCount: 5);

        await using var boundToA = NewContextWith(new PreResolvedTenantContext("A"));
        var visible = await boundToA.Reports.ToListAsync();

        Assert.Equal(3, visible.Count);
        Assert.All(visible, r => Assert.Equal("A", r.TenantId));
    }

    [Fact]
    public async Task IgnoreAllFilters_reveals_soft_deleted_cross_tenant_rows()
    {
        // The fully-unfiltered escape hatch (IgnoreQueryFilters() with no names) lifts BOTH filters —
        // documenting that the surgical single-name bypass above is a deliberate, narrower choice.
        await SeedReportsAsync("A", rowCount: 1);
        await SeedReportsAsync("B", rowCount: 1);
        await using (var ctx = NewContextFor("A"))
        {
            ctx.Reports.Remove(await ctx.Reports.SingleAsync());
            await ctx.SaveChangesAsync();
        }

        await using var host = NewContextWith(new PreResolvedTenantContext("__host__"));
        var everything = await host.Reports.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, everything.Count);
        Assert.Contains(everything, r => r.IsDeleted);
    }
}
