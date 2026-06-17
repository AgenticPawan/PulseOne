using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.Infrastructure.Persistence;

/// <summary>
/// Helpers for constructing an <see cref="ApplicationDbContext"/> outside an HTTP request —
/// notably the MigrationRunner, which migrates each shard by raw connection string.
/// During migration there is no ambient tenant or user, so a design-time stub is supplied.
/// </summary>
public static class ApplicationDbContextFactory
{
    /// <summary>Build a context bound to an explicit shard connection string (no request scope).</summary>
    public static ApplicationDbContext CreateForConnection(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        return new ApplicationDbContext(options, DesignTimeTenant.Instance, DesignTimeUser.Instance);
    }

    private sealed class DesignTimeTenant : ITenantContext
    {
        public static readonly DesignTimeTenant Instance = new();
        public bool IsResolved => false;
        public string TenantId => throw new TenantResolutionException(
            "ApplicationDbContext was constructed for migration; tenant access is not permitted here.");
        public void Resolve(string tenantId) =>
            throw new TenantResolutionException("Cannot resolve a tenant on a migration-only context.");
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public static readonly DesignTimeUser Instance = new();
        public string UserId => "migration-runner";
        public string? TenantId => null;
        public bool IsHostOperator => true;
    }
}

/// <summary>
/// EF Core design-time factory so <c>dotnet ef migrations add</c> can construct the context
/// without a running host. Uses a localdb placeholder; runtime uses Key Vault-backed strings.
/// </summary>
public sealed class ApplicationDbContextDesignFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // <KEY_VAULT_REFERENCE> ConnectionStrings--Shard01 — design-time placeholder only.
        const string designTimeConnection =
            "Server=(localdb)\\mssqllocaldb;Database=PulseOne_Shard_DesignTime;Trusted_Connection=True;";
        return ApplicationDbContextFactory.CreateForConnection(designTimeConnection);
    }
}
