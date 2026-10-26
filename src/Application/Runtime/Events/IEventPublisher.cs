namespace Basil.Application.Runtime.Events;

/// <summary>
///     Publishes an event to every handler that observes it; returns at once, the handlers run later and in the order
///     the events were published.
/// </summary>
/// <typeparam name="T">The event category published.</typeparam>
public interface IEventPublisher<in T> where T : Event
{
	/// <summary>Publishes an event to every handler that observes it.</summary>
	/// <param name="event">The event to publish.</param>
	void Publish(T @event);
}