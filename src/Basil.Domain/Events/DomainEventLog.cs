namespace Basil.Domain.Events;

/// <summary>
///     An append-only log an aggregate uses to record domain events as they occur, backing its
///     <see cref="IHasDomainEvents" /> implementation.
/// </summary>
public sealed class DomainEventLog
{
	private readonly List<IDomainEvent> _events = [];

	/// <summary>Gets the events recorded so far, in the order they occurred.</summary>
	public IReadOnlyList<IDomainEvent> Events => _events;

	/// <summary>Records a domain event that has occurred.</summary>
	/// <param name="domainEvent">The event to record.</param>
	public void Record(IDomainEvent domainEvent)
	{
		_events.Add(domainEvent);
	}

	/// <summary>Clears every recorded event.</summary>
	public void Clear()
	{
		_events.Clear();
	}
}
