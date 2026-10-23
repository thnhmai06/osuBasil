using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Multiplayer.Rooms;

namespace Basil.Application.Runtime.Multiplayer.Events;

/// <summary>Something happened to a room.</summary>
public abstract record RoomEvent(Room Room) : Event;