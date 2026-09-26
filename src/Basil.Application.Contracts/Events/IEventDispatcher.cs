using Basil.Domain.Events;

namespace Basil.Application.Contracts.Events;

/// <summary>
///     Routes a recorded domain event to every <see cref="IDomainEventHandler{TEvent}" /> registered
///     for its concrete type.
/// </summary>
/// <remarks>
///     Handlers for the same event run in the order they were recorded relative to other events. A
///     handler that throws is caught and does not prevent the other handlers for the same event from
///     running.
/// </remarks>
public interface IEventDispatcher
{
	/// <summary>Dispatches a domain event to every handler registered for its type.</summary>
	/// <param name="domainEvent">The event to dispatch.</param>
	/// <param name="cancellationToken">A token that cancels the dispatch.</param>
	Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}