namespace Basil.Application.Common.Events;

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

	/// <summary>
	///     Gets whether the publisher can take the events of new work. An entry point whose events have durable
	///     consequences refuses its request when this is <see langword="false" />, because an event published while the
	///     queue is full is dropped.
	/// </summary>
	bool HasRoom => true;
}