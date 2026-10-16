using Basil.Application.Services.Contracts.Events;

namespace Basil.Infrastructure.Runtime.Events;

/// <summary>Reacts to the events of one category emitted by the application's services.</summary>
/// <typeparam name="T">The event category handled.</typeparam>
public interface IEventHandler<in T> where T : Event
{
	/// <summary>Handles one event.</summary>
	/// <param name="event">The event.</param>
	/// <param name="cancellationToken">A token that cancels the handling.</param>
	ValueTask HandleAsync(T @event, CancellationToken cancellationToken);
}