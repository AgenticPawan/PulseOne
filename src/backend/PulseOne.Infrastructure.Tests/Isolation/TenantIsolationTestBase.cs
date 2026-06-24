using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using NSubstitute;
using PulseOne.CoreDomain.Entities;
using PulseOne.Infrastructure.Persistence;
using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.MultiTenancy;
using Xunit;

namespace PulseOne.Infrastructure.Tests.Isolation;

/// <summary>
/// Base for the tenant-isolation suite (blueprint §7.2). It proves the EF Core 10 "Tenant" and
/// "SoftDelete" named query filters are REAL — the v1 <c>return null</c> stub would fail every test
/// here. Uses a single shared SQLite in-memory connection (a REAL relational provider) so the named
/// filters are enforced exactly as in production; <c>UseInMemoryDatabase</c> is intentionally NOT
/// used (it does not honour query filters the same way).
/// </summary>
/// <remarks>
/// <para>SQLite <c>:memory:</c> databases are scoped to a single open connection. We hold one open
/// connection for the lifetime of a test class so every <see cref="ApplicationDbContext"/> built in
/// the test sees the same schema and rows. The connection is opened in <see cref="InitializeAsync"/>
/// and disposed in <see cref="DisposeAsync"/>, so each test class is independent.</para>
/// </remarks>
public abstract class TenantIsolationTestBase : IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    public Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // Create the schema once on the shared connection.
        using var ctx = NewContextFor("schema-bootstrap");
        ctx.Database.EnsureCreated();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _connection.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds an <see cref="ApplicationDbContext"/> bound to a fail-closed <see cref="TenantContext"/>
    /// resolved to <paramref name="tenantId"/> — exactly how the request pipeline wires it. The
    /// <see cref="ICurrentUser"/> is an NSubstitute double (the project's mandated mocking library).
    /// </summary>
    protected ApplicationDbContext NewContextFor(string tenantId, string userId = "test-user")
    {
        var tenant = new TenantContext();
        tenant.Resolve(tenantId);
        return BuildContext(tenant, userId);
    }

    /// <summary>
    /// Builds a context over an arbitrary <see cref="ITenantContext"/> — used by the host cross-tenant
    /// tests to supply a pre-resolved, non-throwing context (the host's sanctioned path).
    /// </summary>
    protected ApplicationDbContext NewContextWith(ITenantContext tenant, string userId = "host-operator") =>
        BuildContext(tenant, userId);

    private ApplicationDbContext BuildContext(ITenantContext tenant, string userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.TenantId.Returns(tenant.IsResolved ? SafeTenant(tenant) : null);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            // The production model pins SQL Server column types (nvarchar(max)); rewrite those to the
            // provider default so EnsureCreated emits valid SQLite DDL. This touches ONLY physical
            // column typing — the named query filters under test are completely untouched.
            .ReplaceService<IModelCustomizer, SqliteColumnTypeStrippingModelCustomizer>()
            .Options;

        return new ApplicationDbContext(options, tenant, currentUser);
    }

    private static string? SafeTenant(ITenantContext tenant)
    {
        try { return tenant.TenantId; }
        catch (TenantResolutionException) { return null; }
    }

    /// <summary>
    /// Seeds <paramref name="rowCount"/> reports for <paramref name="tenantId"/> through the normal
    /// write path (so the tenant stamp + audit rows are produced exactly as in production).
    /// </summary>
    protected async Task SeedReportsAsync(string tenantId, int rowCount)
    {
        await using var ctx = NewContextFor(tenantId);
        for (var i = 0; i < rowCount; i++)
        {
            ctx.Reports.Add(new Report { ReportName = $"{tenantId}-report-{i}", ReportType = "Excel" });
        }
        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// A pre-resolved tenant context that NEVER throws — models the host's <c>FixedTenantContext</c>
    /// (which is internal to Infrastructure) for the sanctioned cross-tenant path tests.
    /// </summary>
    protected sealed class PreResolvedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
        public bool IsResolved => true;
        public void Resolve(string tenantId) { /* immutable once constructed */ }
    }

    /// <summary>
    /// Strips SQL Server-specific column types (e.g. <c>nvarchar(max)</c>) from the model so the SQLite
    /// provider can generate valid DDL in <c>EnsureCreated</c>. Runs the standard customizer first, then
    /// clears column-type annotations only — query filters, keys, and indexes are left intact.
    /// </summary>
    private sealed class SqliteColumnTypeStrippingModelCustomizer(ModelCustomizerDependencies dependencies)
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
                    property.SetColumnType(null); // let SQLite use its default TEXT affinity
            }
        }
    }
}
