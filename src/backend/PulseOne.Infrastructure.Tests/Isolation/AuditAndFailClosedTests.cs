using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PulseOne.CoreDomain.Entities;
using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.MultiTenancy;
using Xunit;

namespace PulseOne.Infrastructure.Tests.Isolation;

/// <summary>
/// Audit-writer and fail-closed assertions (blueprint §6.2 / §7.2). Proves the audit writer captures
/// real key/old/new snapshots (v1 wrote nothing), that audit rows are NOT tenant-filtered, and that a
/// write attempted under an UNRESOLVED tenant context throws rather than silently leaking into a
/// "default" bucket (CLAUDE.md security rule #2).
/// </summary>
[Trait("Category", "Isolation")]
public sealed class AuditAndFailClosedTests : TenantIsolationTestBase
{
    [Fact]
    public async Task AuditLog_captures_KeyValues_NewValues_on_insert()
    {
        await using (var ctx = NewContextFor("A"))
        {
            ctx.Reports.Add(new Report { ReportName = "audited", ReportType = "Excel" });
            await ctx.SaveChangesAsync();
        }

        await using var verify = NewContextFor("A");
        var audit = await verify.AuditLogs.SingleAsync(a => a.TableName == "Reports" && a.Action == "Added");

        Assert.NotNull(audit.KeyValues);
        Assert.NotNull(audit.NewValues);
        Assert.Null(audit.OldValues); // inserts have no prior state
        Assert.Contains("ReportName", audit.NewValues);
    }

    [Fact]
    public async Task AuditLog_captures_OldValues_NewValues_KeyValues_on_modify()
    {
        await SeedReportsAsync("A", rowCount: 1);

        await using (var ctx = NewContextFor("A"))
        {
            var report = await ctx.Reports.SingleAsync();
            report.Status = "Completed";
            await ctx.SaveChangesAsync();
        }

        await using var verify = NewContextFor("A");
        var modify = await verify.AuditLogs.SingleAsync(a => a.Action == "Modified");

        Assert.NotNull(modify.KeyValues);
        Assert.NotNull(modify.OldValues);
        Assert.NotNull(modify.NewValues);

        // The before/after snapshots are genuine JSON maps (host Audit Browser reads them).
        var oldValues = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(modify.OldValues!);
        var newValues = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(modify.NewValues!);
        Assert.NotNull(oldValues);
        Assert.NotNull(newValues);
        Assert.Equal("Pending", oldValues!["Status"].GetString());
        Assert.Equal("Completed", newValues!["Status"].GetString());
    }

    [Fact]
    public async Task AuditLog_is_NOT_filtered_by_tenant_filter()
    {
        // Two tenants each write a row -> each produces an audit row, both readable from any context
        // because AuditLog is excluded from the "Tenant" named filter.
        await SeedReportsAsync("A", rowCount: 1);
        await SeedReportsAsync("B", rowCount: 1);

        await using var asA = NewContextFor("A");
        var allAudit = await asA.AuditLogs.ToListAsync();

        // Tenant A's context can see audit rows stamped for BOTH tenants (no tenant filter on AuditLog).
        Assert.Contains(allAudit, a => a.TenantId == "A");
        Assert.Contains(allAudit, a => a.TenantId == "B");
    }

    [Fact]
    public async Task TenantContext_throws_when_unresolved()
    {
        var unresolved = new TenantContext();
        Assert.False(unresolved.IsResolved);
        Assert.Throws<TenantResolutionException>(() => _ = unresolved.TenantId);
    }

    [Fact]
    public async Task SaveChanges_under_unresolved_tenant_throws_and_does_not_persist()
    {
        var unresolved = new TenantContext(); // never .Resolve(...)d — fail-closed
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns("ghost");

        await using var ctx = NewContextWith(unresolved, userId: "ghost");
        ctx.Reports.Add(new Report { ReportName = "should-not-persist", ReportType = "Excel" });

        // Stamping reads CurrentTenantId -> TenantContext.TenantId -> throws. The write is rejected
        // BEFORE any row reaches the database (no silent "default" tenant).
        await Assert.ThrowsAsync<TenantResolutionException>(() => ctx.SaveChangesAsync());

        await using var verify = NewContextFor("should-not-persist");
        Assert.Equal(0, await verify.Reports.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Reading_under_unresolved_tenant_throws()
    {
        var unresolved = new TenantContext();
        await using var ctx = NewContextWith(unresolved);

        // The tenant filter evaluates CurrentTenantId per query; an unresolved context cannot read.
        await Assert.ThrowsAsync<TenantResolutionException>(() => ctx.Reports.ToListAsync());
    }
}
