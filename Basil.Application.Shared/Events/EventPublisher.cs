using Mediator;

namespace Basil.Application.Shared.Events;

public interface IEventPublisher<in T> where T : Event
{
	void Publish(T @event);
}

public sealed class EventPublisher(Lazy<IPublisher> publisher, TimeProvider clock) : IEventPublisher<Event>
{
	public void Publish(Event @event)
	{
		@event.Timestamp = clock.GetUtcNow();
		var published = publisher.Value.Publish(@event);
		if (!published.IsCompletedSuccessfully)
			throw new InvalidOperationException("Publishing an event must only queue its delivery.");
	}
}