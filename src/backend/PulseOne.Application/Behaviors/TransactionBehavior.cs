using MediatR;
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
/// </remarks>
public sealed class TransactionBehavior<TRequest, TResponse>(IApplicationDbContext dbContext)
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

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        var response = await next();

        // Commit only on the success path; an exception escaping `next()` skips this and the
        // `await using` disposal rolls the transaction back.
        await transaction.CommitAsync(cancellationToken);
        return response;
    }
}
