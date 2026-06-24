using Hangfire;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NSubstitute;
using PulseOne.Application.Features.TenantPortal;
using PulseOne.CoreDomain.Entities;
using PulseOne.Infrastructure.Authorization;
using PulseOne.Infrastructure.Persistence;
using PulseOne.Infrastructure.Persistence.Catalog;
using PulseOne.Infrastructure.TenantPortal;
using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.MultiTenancy;
using Xunit;

namespace PulseOne.Infrastructure.Tests.Isolation;

/// <summary>
/// Tenant-isolation proof for <see cref="TenantPortalService"/> (Phase 6 tenant endpoints). The
/// service runs inside the request's resolved tenant and relies entirely on the EF Core "Tenant" +
/// "SoftDelete" named query filters for isolation — it never bypasses them. These tests confirm that:
/// one tenant's reports / API keys / team / settings are invisible to another tenant, that a tenant
/// cannot mutate another's rows by id, and that an UNRESOLVED tenant context fails closed (throws)
/// rather than leaking across the whole shard. Backed by the same shared-connection SQLite harness as
/// the rest of the isolation suite (a REAL relational provider, so the filters behave as in prod).
/// </summary>
[Trait("Category", "Isolation")]
public sealed class TenantPortalServiceIsolationTests : TenantIsolationTestBase, IDisposable
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";

    // The catalog lives on its own shared in-memory connection (separate schema from the shard).
    private readonly SqliteConnection _catalogConnection;

    public TenantPortalServiceIsolationTests()
    {
        _catalogConnection = new SqliteConnection("Data Source=:memory:");
        _catalogConnection.Open();

        using var seed = NewCatalogContext();
        seed.Database.EnsureCreated();
        seed.TenantShards.AddRange(
            Shard(TenantA, TenantTier.Pro),
            Shard(TenantB, TenantTier.Enterprise));
        seed.SaveChanges();
    }

    public void Dispose() => _catalogConnection.Dispose();

    // ---- Reports -----------------------------------------------------------------------------

    [Fact]
    public async Task ListReportsAsync_ReturnsOnlyCallersTenantReports()
    {
        await SeedReportsAsync(TenantA, 3);
        await SeedReportsAsync(TenantB, 2);

        var a = await Service(TenantA).ListReportsAsync(new ReportListQuery(1, 50));
        var b = await Service(TenantB).ListReportsAsync(new ReportListQuery(1, 50));

        Assert.Equal(3, a.TotalCount);
        Assert.Equal(3, a.Items.Count);
        Assert.All(a.Items, r => Assert.StartsWith(TenantA, r.ReportName, StringComparison.Ordinal));
        Assert.Equal(2, b.TotalCount);
        Assert.All(b.Items, r => Assert.StartsWith(TenantB, r.ReportName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetReportDownloadAsync_CannotReachAnotherTenantsReport()
    {
        var bReportId = await SeedCompletedReportAsync(TenantB, "https://b.blob/sas");

        // The owner can fetch its SAS url; tenant A cannot see the row at all (filtered out -> null).
        Assert.NotNull(await Service(TenantB).GetReportDownloadAsync(bReportId));
        Assert.Null(await Service(TenantA).GetReportDownloadAsync(bReportId));
    }

    [Fact]
    public async Task DeleteReportAsync_CannotDeleteAnotherTenantsReport()
    {
        await SeedReportsAsync(TenantB, 1);
        var bReportId = await FirstReportIdAsync(TenantB);

        var deleted = await Service(TenantA).DeleteReportAsync(bReportId);

        Assert.False(deleted); // invisible to A -> reported as not found, never soft-deleted
        var stillThere = await Service(TenantB).ListReportsAsync(new ReportListQuery(1, 50));
        Assert.Equal(1, stillThere.TotalCount);
    }

    // ---- Settings: API keys ------------------------------------------------------------------

    [Fact]
    public async Task ApiKeys_AreTenantScoped_AndCannotBeRevokedAcrossTenants()
    {
        var aKey = await Service(TenantA).CreateApiKeyAsync("A key");
        var bKey = await Service(TenantB).CreateApiKeyAsync("B key");

        var aKeys = await Service(TenantA).GetApiKeysAsync();
        Assert.Single(aKeys);
        Assert.Equal("A key", aKeys[0].Name);

        // A cannot revoke B's key by id (B's row is invisible to A's tenant-filtered context).
        Assert.False(await Service(TenantA).RevokeApiKeyAsync(bKey.Id));
        Assert.Single(await Service(TenantB).GetApiKeysAsync());

        // Sanity: the owner CAN revoke its own key, and it then drops out of the list (soft-deleted).
        Assert.True(await Service(TenantA).RevokeApiKeyAsync(aKey.Id));
        Assert.Empty(await Service(TenantA).GetApiKeysAsync());
    }

    // ---- Team --------------------------------------------------------------------------------

    [Fact]
    public async Task GetTeamAsync_ReturnsOnlyCallersTenantMembers()
    {
        await SeedRoleGrantAsync(TenantA, "user-a-1");
        await SeedRoleGrantAsync(TenantB, "user-b-1");

        var aTeam = await Service(TenantA).GetTeamAsync();
        var bTeam = await Service(TenantB).GetTeamAsync();

        Assert.Contains(aTeam, m => m.UserId == "user-a-1");
        Assert.DoesNotContain(aTeam, m => m.UserId == "user-b-1");
        Assert.Contains(bTeam, m => m.UserId == "user-b-1");
        Assert.DoesNotContain(bTeam, m => m.UserId == "user-a-1");
    }

    // ---- Settings: profile -------------------------------------------------------------------

    [Fact]
    public async Task Profile_IsTenantScoped()
    {
        await Service(TenantA).UpdateProfileAsync(new CompanyProfileDto("Acme Corp", "ops@acme.test", "555", null));

        var aProfile = await Service(TenantA).GetProfileAsync();
        var bProfile = await Service(TenantB).GetProfileAsync();

        Assert.Equal("Acme Corp", aProfile.CompanyName);
        // B never saved a profile, so it reads its OWN default (catalog name), never A's saved value.
        Assert.NotEqual("Acme Corp", bProfile.CompanyName);
    }

    // ---- Dashboard ---------------------------------------------------------------------------

    [Fact]
    public async Task DashboardSummary_CountsOnlyCallersReports_AndReflectsCallersTier()
    {
        await SeedReportsAsync(TenantA, 3);
        await SeedReportsAsync(TenantB, 2);

        var a = await Service(TenantA).GetDashboardSummaryAsync();
        var b = await Service(TenantB).GetDashboardSummaryAsync();

        Assert.Equal(3, a.ReportsGenerated);
        Assert.Equal("Pro", a.CurrentPlan);          // tenant-a's catalog tier
        Assert.Equal(2, b.ReportsGenerated);
        Assert.Equal("Enterprise", b.CurrentPlan);   // tenant-b's catalog tier
    }

    // ---- Fail-closed -------------------------------------------------------------------------

    [Fact]
    public async Task UnresolvedTenant_FailsClosed_RatherThanLeaking()
    {
        await SeedReportsAsync(TenantA, 2);

        // A context whose tenant was never resolved must THROW when the tenant filter is evaluated,
        // never silently return every tenant's rows.
        await using var db = NewContextWith(new TenantContext());
        var svc = new TenantPortalService(
            db, NewCatalogContext(), Substitute.For<ICurrentUser>(), Substitute.For<IBackgroundJobClient>());

        await Assert.ThrowsAsync<TenantResolutionException>(
            () => svc.ListReportsAsync(new ReportListQuery(1, 50)));
    }

    // ---- helpers -----------------------------------------------------------------------------

    private TenantPortalService Service(string tenantId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns($"user-{tenantId}");
        user.TenantId.Returns(tenantId);
        return new TenantPortalService(
            NewContextFor(tenantId), NewCatalogContext(), user, Substitute.For<IBackgroundJobClient>());
    }

    private async Task<string> SeedCompletedReportAsync(string tenantId, string outputUrl)
    {
        await using var ctx = NewContextFor(tenantId);
        var report = new Report
        {
            ReportName = $"{tenantId}-completed",
            ReportType = "Excel",
            Status = "Completed",
            OutputUrl = outputUrl,
            CompletedAt = DateTimeOffset.UtcNow,
        };
        ctx.Reports.Add(report);
        await ctx.SaveChangesAsync();
        return report.Id;
    }

    private async Task<string> FirstReportIdAsync(string tenantId)
    {
        await using var ctx = NewContextFor(tenantId);
        return await ctx.Reports.Select(r => r.Id).FirstAsync();
    }

    private async Task SeedRoleGrantAsync(string tenantId, string userId)
    {
        await using var ctx = NewContextFor(tenantId);
        // TenantRole/TenantUserRole are IMultiTenantEntity but not BaseEntity, so TenantId is set
        // explicitly (it is not auto-stamped). The "Tenant" filter still scopes reads by it.
        var role = new TenantRole { TenantId = tenantId, Name = "Admin", Permissions = ["reports.view"] };
        ctx.TenantRoles.Add(role);
        await ctx.SaveChangesAsync();

        ctx.TenantUserRoles.Add(new TenantUserRole { TenantId = tenantId, UserId = userId, RoleId = role.Id });
        await ctx.SaveChangesAsync();
    }

    private TenantCatalogDbContext NewCatalogContext()
    {
        var options = new DbContextOptionsBuilder<TenantCatalogDbContext>()
            .UseSqlite(_catalogConnection)
            .ReplaceService<IModelCustomizer, CatalogSqliteColumnTypeStrippingModelCustomizer>()
            .Options;
        return new TenantCatalogDbContext(options);
    }

    private static TenantShard Shard(string tenantId, TenantTier tier) => new()
    {
        TenantId = tenantId,
        Name = $"{tenantId} Inc",
        AdminEmail = $"admin@{tenantId}.test",
        ShardConnectionString = "Data Source=:memory:",
        ShardLabel = "Shard01",
        Region = "westindia",
        Tier = tier,
        Status = TenantStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true,
    };

    /// <summary>Strips SQL Server column types so the catalog schema builds on SQLite (same approach
    /// as the shard customizer in the base class; touches typing only).</summary>
    private sealed class CatalogSqliteColumnTypeStrippingModelCustomizer(ModelCustomizerDependencies dependencies)
        : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entityType.GetProperties())
            {
                var columnType = property.GetColumnType();
                if (columnType is not null && columnType.Contains("nvarchar", StringComparison.OrdinalIgnoreCase))
                    property.SetColumnType(null);
            }
        }
    }
}
