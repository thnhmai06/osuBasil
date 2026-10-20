using System.Threading.Channels;
using Basil.Application.Objects.Multiplayer.Events;
using Basil.Application.Objects.Multiplayer.Rooms;

namespace Basil.Application.Services.Multiplayer.Rooms;

/// <summary>The one event stream of rooms, shared by the room service and its children.</summary>
internal sealed class RoomEventStream(TimeProvider time) : IRoomEventSink
{
	private readonly Channel<RoomEvent> _events = Channel.CreateUnbounded<RoomEvent>();

	/// <summary>Gets the reader of the events emitted so far.</summary>
	internal ChannelReader<RoomEvent> Reader => _events.Reader;

	/// <summary>Emits an event to the stream.</summary>
	/// <param name="event">The event to emit.</param>
	public void Emit(RoomEvent @event)
	{
		_events.Writer.TryWrite(@event with { Timestamp = time.GetUtcNow() });
	}
}
