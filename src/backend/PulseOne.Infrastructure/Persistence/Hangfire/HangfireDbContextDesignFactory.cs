using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PulseOne.Infrastructure.Persistence.Hangfire;

/// <summary>
/// EF Core design-time factory for the Hangfire context so the PulseOne-authored tables in the
/// Hangfire DB (the dead-letter table) can be scaffolded with <c>dotnet ef</c>. The runtime
/// connection string is Key Vault-backed ("ConnectionStrings--Hangfire"); this localdb value is
/// design-time only. Hangfire's own job tables are NOT modeled here — it provisions those itself.
/// </summary>
public sealed class HangfireDbContextDesignFactory : IDesignTimeDbContextFactory<HangfireDbContext>
{
    public HangfireDbContext CreateDbContext(string[] args)
    {
        // <KEY_VAULT_REFERENCE> ConnectionStrings--Hangfire — design-time placeholder only.
        const string designTimeConnection =
            "Server=(localdb)\\mssqllocaldb;Database=PulseOne_Hangfire_DesignTime;Trusted_Connection=True;";

        var options = new DbContextOptionsBuilder<HangfireDbContext>()
            .UseSqlServer(designTimeConnection)
            .Options;

        return new HangfireDbContext(options);
    }
}
