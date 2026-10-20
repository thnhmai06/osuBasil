using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer.Rooms;

namespace Basil.Application.Objects.Multiplayer.Events;

/// <summary>Something happened to a room.</summary>
public abstract record RoomEvent(Room Room) : Event
{
	/// <summary>Gets the moment the event happened.</summary>
	public DateTimeOffset Timestamp { get; init; }
}
