using Microsoft.EntityFrameworkCore;
using PulseOne.CoreDomain.Entities;
using PulseOne.SharedKernel.MultiTenancy;
using Xunit;

namespace PulseOne.Infrastructure.Tests.Isolation;

/// <summary>
/// The tenant-isolation suite the blueprint §7.2 mandates: it proves the EF Core 10 named query
/// filters fail-closed. CLAUDE.md security rules #2 and #7 live or die on these tests — the v1
/// <c>return null</c> filter stub would fail every cross-tenant and soft-delete assertion here.
/// Runs on a REAL SQLite provider so the filters compose exactly as in production.
/// </summary>
[Trait("Category", "Isolation")]
public sealed class TenantQueryFilterTests : TenantIsolationTestBase
{
    [Fact]
    public async Task Query_as_tenant_A_never_returns_tenant_B_rows()
    {
        await SeedReportsAsync("A", rowCount: 3);
        await SeedReportsAsync("B", rowCount: 5);

        await using var asA = NewContextFor("A");
        var visible = await asA.Reports.ToListAsync();

        // The "Tenant" named filter restricts reads to the resolved tenant — B's 5 rows are invisible.
        Assert.Equal(3, visible.Count);
        Assert.All(visible, r => Assert.Equal("A", r.TenantId));
    }

    [Fact]
    public async Task Query_as_tenant_B_sees_only_tenant_B_rows()
    {
        await SeedReportsAsync("A", rowCount: 3);
        await SeedReportsAsync("B", rowCount: 5);

        await using var asB = NewContextFor("B");
        var visible = await asB.Reports.ToListAsync();

        Assert.Equal(5, visible.Count);
        Assert.All(visible, r => Assert.Equal("B", r.TenantId));
    }

    [Fact]
    public async Task Soft_deleted_rows_are_invisible_to_queries()
    {
        await SeedReportsAsync("A", rowCount: 2);

        // Soft-delete one row through the normal write path (hard delete is converted to soft delete).
        await using (var ctx = NewContextFor("A"))
        {
            var first = await ctx.Reports.FirstAsync();
            ctx.Reports.Remove(first);
            await ctx.SaveChangesAsync();
        }

        await using var asA = NewContextFor("A");
        var visible = await asA.Reports.ToListAsync();

        // The "SoftDelete" named filter hides IsDeleted rows; it composes WITH the tenant filter.
        Assert.Single(visible);
        Assert.All(visible, r => Assert.False(r.IsDeleted));

        // Bypassing ONLY the SoftDelete filter reveals the deleted row (still tenant-scoped).
        var includingDeleted = await asA.Reports.IgnoreQueryFilters(["SoftDelete"]).ToListAsync();
        Assert.Equal(2, includingDeleted.Count);
        Assert.Contains(includingDeleted, r => r.IsDeleted);
    }

    [Fact]
    public async Task SaveChanges_stamps_TenantId_automatically()
    {
        // The caller never sets TenantId; the DbContext stamps it from the resolved tenant on insert.
        await using (var ctx = NewContextFor("contoso"))
        {
            ctx.Reports.Add(new Report { ReportName = "unstamped", ReportType = "Pdf" });
            await ctx.SaveChangesAsync();
        }

        await using var verify = NewContextFor("contoso");
        var report = await verify.Reports.SingleAsync();
        Assert.Equal("contoso", report.TenantId);
        Assert.Equal("test-user", report.CreatedBy);
        Assert.NotEqual(default, report.CreatedAt);
    }

    [Fact]
    public async Task SaveChanges_converts_hard_delete_to_soft_delete()
    {
        await SeedReportsAsync("A", rowCount: 1);

        string id;
        await using (var ctx = NewContextFor("A"))
        {
            var report = await ctx.Reports.SingleAsync();
            id = report.Id;
            ctx.Reports.Remove(report); // requests a HARD delete
            await ctx.SaveChangesAsync();
        }

        // The row must STILL exist physically (soft delete), with IsDeleted/DeletedAt set.
        await using var verify = NewContextFor("A");
        var row = await verify.Reports
            .IgnoreQueryFilters(["SoftDelete"])
            .SingleAsync(r => r.Id == id);
        Assert.True(row.IsDeleted);
        Assert.NotNull(row.DeletedAt);
    }

    [Fact]
    public async Task Tenant_filter_blocks_cross_tenant_read_even_by_primary_key()
    {
        await SeedReportsAsync("A", rowCount: 1);
        string aId;
        await using (var ctx = NewContextFor("A"))
            aId = (await ctx.Reports.SingleAsync()).Id;

        // Tenant B knows A's primary key but still cannot read the row — the filter is not bypassable
        // by key lookup.
        await using var asB = NewContextFor("B");
        var leaked = await asB.Reports.FirstOrDefaultAsync(r => r.Id == aId);
        Assert.Null(leaked);
    }
}
