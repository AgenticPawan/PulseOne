using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PulseOne.Infrastructure.Persistence.Catalog;

/// <summary>
/// EF Core design-time factory for the Tenant Catalog context so migrations can be
/// scaffolded with <c>dotnet ef</c>. The runtime connection string is Key Vault-backed
/// ("ConnectionStrings--TenantCatalog"); this localdb value is design-time only.
/// </summary>
public sealed class TenantCatalogDbContextDesignFactory
    : IDesignTimeDbContextFactory<TenantCatalogDbContext>
{
    public TenantCatalogDbContext CreateDbContext(string[] args)
    {
        // <KEY_VAULT_REFERENCE> ConnectionStrings--TenantCatalog — design-time placeholder only.
        const string designTimeConnection =
            "Server=(localdb)\\mssqllocaldb;Database=PulseOne_TenantCatalog_DesignTime;Trusted_Connection=True;";

        var options = new DbContextOptionsBuilder<TenantCatalogDbContext>()
            .UseSqlServer(designTimeConnection)
            .Options;

        return new TenantCatalogDbContext(options);
    }
}
