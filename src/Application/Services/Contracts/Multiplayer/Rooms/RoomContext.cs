using Basil.Application.Services.Contracts.Multiplayer.Events;

namespace Basil.Application.Services.Contracts.Multiplayer.Rooms;

/// <summary>The runtime services a room needs to emit events and check permissions.</summary>
/// <param name="Clock">The clock for checking current time.</param>
/// <param name="Events">The event sink for emitting room events.</param>
internal sealed record RoomContext(TimeProvider Clock, IRoomEventSink Events);

/// <summary>Emits room events to the event stream.</summary>
internal interface IRoomEventSink
{
	/// <summary>Emits a room event.</summary>
	/// <param name="event">The event to emit.</param>
	void Emit(RoomEvent @event);
}