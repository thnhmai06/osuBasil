using System.Threading.Channels;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Services.Contracts.Users;

namespace Basil.Application.Services.Implementations.Users;

/// <summary>The one event stream of users, shared by the user service and its children.</summary>
internal sealed class UserEventStream : IUserEventSink
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

	void IUserEventSink.Emit(UserEvent @event) => Emit(@event);
}
