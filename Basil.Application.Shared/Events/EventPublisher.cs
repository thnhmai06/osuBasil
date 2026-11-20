namespace Basil.Application.Shared.Events;

public interface IEventPublisher<in T> where T : Event
{
	void Publish(T @event);
}