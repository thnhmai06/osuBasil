using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer.Rooms;

namespace Basil.Application.Objects.Multiplayer.Events;

/// <summary>Something happened to a room.</summary>
public abstract record RoomEvent(Room Room) : Event;