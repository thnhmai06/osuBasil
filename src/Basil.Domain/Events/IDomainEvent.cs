namespace Basil.Domain.Events;

/// <summary>
///     Marks a type as a domain event: a record of something business-meaningful that has already
///     happened to an aggregate.
/// </summary>
public interface IDomainEvent;

/// <summary>
///     Exposes the domain events an aggregate has recorded since it was created or since they were
///     last cleared.
/// </summary>
public interface IHasDomainEvents
{
	/// <summary>Gets the domain events recorded so far, in the order they occurred.</summary>
	IReadOnlyList<IDomainEvent> DomainEvents { get; }

	/// <summary>Clears every domain event recorded so far.</summary>
	void ClearDomainEvents();
}
