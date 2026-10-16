using System.Threading.Channels;
using Basil.Application.Services.Contracts.Sessions;

namespace Basil.Application.Services.Implementations.Sessions;

/// <summary>The one event stream of sessions, shared by the session service and its children.</summary>
internal sealed class SessionEventStream
{
	private readonly Channel<UserEvent> _events = Channel.CreateUnbounded<UserEvent>();

	/// <summary>Gets the reader of the events emitted so far.</summary>
	internal ChannelReader<UserEvent> Reader => _events.Reader;

	/// <summary>Emits an event to the stream.</summary>
	/// <param name="event">The event to emit.</param>
	internal void Emit(UserEvent @event)
	{
		_events.Writer.TryWrite(@event);
	}
}