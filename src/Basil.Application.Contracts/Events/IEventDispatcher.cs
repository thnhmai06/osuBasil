using Basil.Application.Models.Events;

namespace Basil.Application.Contracts.Events;

/// <summary>
///     Routes a recorded event to every <see cref="IEventHandler{TEvent}" /> registered for the
///     event's concrete type or any category type it derives from.
/// </summary>
/// <remarks>
///     Handlers for the same event run in the order they were recorded relative to other events. A
///     handler that throws is caught and does not prevent the other handlers for the same event from
///     running.
/// </remarks>
public interface IEventDispatcher<in TEvent> where TEvent : Event
{
	/// <summary>Dispatches an event to every handler registered for its type or a category it inherits.</summary>
	/// <param name="event">The event to dispatch.</param>
	/// <param name="cancellationToken">A token that cancels the dispatch.</param>
	Task DispatchAsync(TEvent @event, CancellationToken cancellationToken = default);
}