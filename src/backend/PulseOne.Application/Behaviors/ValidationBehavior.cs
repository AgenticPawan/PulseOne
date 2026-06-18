using FluentValidation;
using MediatR;

namespace PulseOne.Application.Behaviors;

/// <summary>
/// Second behavior in the pipeline (after <see cref="LoggingBehavior{TRequest,TResponse}"/>):
/// runs every registered <see cref="IValidator{T}"/> for the request and aggregates failures into
/// a single <see cref="ValidationException"/>. Runs before the tenant-scope and transaction
/// behaviors so an invalid request never opens a transaction or touches a handler.
/// </summary>
/// <remarks>
/// No-op when no validator is registered for the request type — only commands/queries that ship a
/// validator pay the cost. Validators are gathered from the same assembly as the handlers via
/// <c>AddValidatorsFromAssembly</c> in <see cref="DependencyInjection"/>.
/// </remarks>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var failures = (await Task.WhenAll(
                validators.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}
