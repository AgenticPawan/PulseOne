using Microsoft.EntityFrameworkCore;

namespace PulseOne.Infrastructure.Persistence.Hangfire;

/// <summary>
/// Builds a short-lived <see cref="HangfireDbContext"/> from the isolated Hangfire connection
/// string. Used by the dead-letter store, which runs inside Hangfire's state-election pipeline —
/// outside any HTTP/DI request scope — so it cannot rely on a scoped context.
/// </summary>
public static class HangfireDbContextFactory
{
    /// <summary>Construct a context bound to the Hangfire DB connection string.</summary>
    public static HangfireDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<HangfireDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        return new HangfireDbContext(options);
    }
}
