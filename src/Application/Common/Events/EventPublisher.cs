using Mediator;

namespace Basil.Application.Common.Events;

/// <summary>Publishes every event of the application: stamps it, then queues its delivery.</summary>
/// <param name="publisher">
///     The mediator that finds the handlers; resolved at first use, because the handlers reach objects
///     that publish.
/// </param>
/// <param name="clock">The clock the events are stamped with.</param>
public sealed class EventPublisher(Lazy<IPublisher> publisher, TimeProvider clock) : IEventPublisher<Event>
{
	/// <inheritdoc />
	public void Publish(Event @event)
	{
		@event.Timestamp = clock.GetUtcNow();
		var published = publisher.Value.Publish(@event);
		if (!published.IsCompletedSuccessfully)
			throw new InvalidOperationException("Publishing an event must only queue its delivery.");
	}
}