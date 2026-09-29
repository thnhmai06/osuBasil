using Basil.Application.Models.Events;

namespace Basil.Application.Contracts.Events;

/// <summary>
///     Handles the consequence of one kind of event: notifying clients, recording history, or
///     otherwise reacting after the fact. The event itself has already happened; a handler never
///     rejects it.
/// </summary>
/// <typeparam name="TEvent">The event type this handler reacts to.</typeparam>
public interface IEventHandler<in TEvent> where TEvent : Event
{
	/// <summary>Reacts to an event that has occurred.</summary>
	/// <param name="event">The event to react to.</param>
	/// <param name="cancellationToken">A token that cancels the reaction.</param>
	Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}
