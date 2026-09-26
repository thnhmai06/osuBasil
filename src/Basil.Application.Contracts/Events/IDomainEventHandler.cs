using Basil.Domain.Events;

namespace Basil.Application.Contracts.Events;

/// <summary>
///     Handles the consequence of one kind of domain event: notifying clients, recording history, or
///     otherwise reacting after the fact. The event itself has already happened; a handler never
///     rejects it.
/// </summary>
/// <typeparam name="TEvent">The concrete domain event type this handler reacts to.</typeparam>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
	/// <summary>Reacts to a domain event that has occurred.</summary>
	/// <param name="domainEvent">The event to react to.</param>
	/// <param name="cancellationToken">A token that cancels the reaction.</param>
	Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}