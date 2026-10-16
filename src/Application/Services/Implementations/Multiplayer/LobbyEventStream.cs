using System.Threading.Channels;
using Basil.Application.Services.Contracts.Multiplayer.Events;

namespace Basil.Application.Services.Implementations.Multiplayer;

/// <summary>The one event stream of the lobby, shared by the lobby service and its children.</summary>
internal sealed class LobbyEventStream(TimeProvider time)
{
	private readonly Channel<LobbyEvent> _events = Channel.CreateUnbounded<LobbyEvent>();

	/// <summary>Gets the reader of the events emitted so far.</summary>
	internal ChannelReader<LobbyEvent> Reader => _events.Reader;

	/// <summary>Emits an event to the stream.</summary>
	/// <param name="event">The event to emit.</param>
	internal void Emit(LobbyEvent @event)
	{
		_events.Writer.TryWrite(@event with { Timestamp = time.GetUtcNow() });
	}
}