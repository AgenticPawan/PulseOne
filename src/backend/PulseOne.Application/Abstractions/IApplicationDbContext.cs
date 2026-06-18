using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace PulseOne.Application.Abstractions;

/// <summary>
/// Application-layer seam over the business-shard <c>ApplicationDbContext</c> (implemented in
/// PulseOne.Infrastructure). CQRS handlers depend on this abstraction so the Application layer
/// never references Infrastructure — preserving the dependency direction
/// Infrastructure → Application → CoreDomain.
/// </summary>
/// <remarks>
/// The concrete context still owns the EF Core 10 named query filters and the audit-writing
/// <see cref="SaveChangesAsync"/> override (blueprint §6.2). Handlers see only <see cref="Set{T}"/>
/// and a single-token save, so tenant stamping/soft-delete/audit all remain centralized.
/// </remarks>
public interface IApplicationDbContext
{
    /// <summary>The tracked set for <typeparamref name="TEntity"/>, with named query filters applied.</summary>
    /// <remarks>
    /// Named <c>Set</c> deliberately to mirror EF Core's <see cref="DbContext.Set{TEntity}()"/> so the
    /// seam is a drop-in for handlers; CA1716 is suppressed for that reason.
    /// </remarks>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
        Justification = "Mirrors EF Core's DbContext.Set<T>() so the abstraction is a drop-in for handlers.")]
    DbSet<TEntity> Set<TEntity>() where TEntity : class;

    /// <summary>
    /// Persists changes. Stamps audit fields, converts hard deletes to soft deletes, and writes
    /// <c>AuditLog</c> rows before delegating to EF Core. Takes a single <see cref="CancellationToken"/>.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a database transaction and returns an <see cref="IAsyncDisposable"/> handle that the
    /// caller commits explicitly (or rolls back on dispose if not committed). Used only by
    /// <c>TransactionBehavior</c> to wrap command handlers; queries never call this.
    /// </summary>
    Task<IApplicationDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A unit-of-work transaction handle exposed through the Application seam so the transaction
/// pipeline behavior can wrap commands without referencing EF Core or Infrastructure directly.
/// Disposing without committing rolls the transaction back.
/// </summary>
public interface IApplicationDbTransaction : IAsyncDisposable
{
    /// <summary>Commits the work performed within this transaction scope.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Rolls back the work performed within this transaction scope.</summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
