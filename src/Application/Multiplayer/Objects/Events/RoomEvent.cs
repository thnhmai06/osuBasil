using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Shared.Events;

namespace Basil.Application.Multiplayer.Objects.Events;

/// <summary>Something happened to a room.</summary>
public abstract record RoomEvent(Room Room) : Event;