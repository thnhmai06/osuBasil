using Basil.Application.Services.Contracts.Events;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Multiplayer.Room;

namespace Basil.Application.Services.Contracts.Multiplayer.Events;

/// <summary>Something happened to a room.</summary>
public abstract record RoomEvent(Room Room) : Event
{
	/// <summary>Gets the moment the event happened.</summary>
	public DateTimeOffset Timestamp { get; init; }
}