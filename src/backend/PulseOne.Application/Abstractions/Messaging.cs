using MediatR;

namespace PulseOne.Application.Abstractions;

/// <summary>
/// Marker for state-mutating requests. Only requests implementing this interface are wrapped in a
/// database transaction by <c>TransactionBehavior</c> — queries deliberately bypass it (a read should
/// never open a write transaction). Commands are <c>record</c> types per CLAUDE.md.
/// </summary>
public interface ICommand<out TResponse> : IRequest<TResponse>;

/// <summary>Marker for read-only requests. Documents intent and keeps queries out of the transaction path.</summary>
public interface IQuery<out TResponse> : IRequest<TResponse>;
