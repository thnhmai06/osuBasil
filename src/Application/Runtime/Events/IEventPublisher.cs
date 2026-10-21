namespace Basil.Application.Runtime.Events;

/// <summary>Publishes an event of a category to every handler of that category; returns at once, the handlers run later and in order.</summary>
/// <typeparam name="T">The event category published.</typeparam>
public interface IEventPublisher<in T> where T : Event
{
	/// <summary>Publishes an event to every handler of its category.</summary>
	/// <param name="event">The event to publish.</param>
	void Publish(T @event);
}