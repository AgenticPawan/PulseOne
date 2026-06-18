using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PulseOne.Application.Abstractions;

namespace PulseOne.Application.Behaviors;

/// <summary>
/// Final behavior in the pipeline (after <see cref="LoggingBehavior{TRequest,TResponse}"/> and
/// <see cref="ValidationBehavior{TRequest,TResponse}"/>): wraps state-mutating
/// <see cref="ICommand{TResponse}"/> requests in a database transaction so the command's writes and
/// the <c>AuditLog</c> rows produced by <c>ApplicationDbContext.SaveChangesAsync</c> commit
/// atomically. If the handler throws, the transaction is disposed without committing and rolls back.
/// </summary>
/// <remarks>
/// Queries (<see cref="IQuery{TResponse}"/>) are not <see cref="ICommand{TResponse}"/> and pass
/// straight through — a read must never open a write transaction. The marker check, not the request
/// name, decides: only commands are transactional.
/// <para>
/// LAZY DB RESOLUTION (Phase 4): the <see cref="IApplicationDbContext"/> is resolved from the request
/// scope ONLY when the request is actually a command, not in the behavior's constructor. The
/// tenant-bound <c>ApplicationDbContext</c> registration eagerly throws <c>TenantResolutionException</c>
/// when no tenant is resolved, so eager injection here would fault the pipeline for legitimately
/// tenant-less, non-mutating requests such as the anonymous Razorpay webhook (which fast-acks and
/// enqueues — security rule #6 — and never opens a transaction). Deferring resolution keeps the
/// pipeline fail-closed for commands while not coupling read/enqueue-only requests to a tenant.
/// </para>
/// </remarks>
public sealed class TransactionBehavior<TRequest, TResponse>(IServiceProvider serviceProvider)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ICommand<TResponse>)
            return await next();

        // Resolve the (tenant-bound) DbContext only for commands — see remarks.
        var dbContext = serviceProvider.GetRequiredService<IApplicationDbContext>();
        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        var response = await next();

        // Commit only on the success path; an exception escaping `next()` skips this and the
        // `await using` disposal rolls the transaction back.
        await transaction.CommitAsync(cancellationToken);
        return response;
    }
}
