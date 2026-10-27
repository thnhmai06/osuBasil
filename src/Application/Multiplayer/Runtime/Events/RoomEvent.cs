using Basil.Application.Common.Events;
using Basil.Application.Multiplayer.Runtime.Rooms;

namespace Basil.Application.Multiplayer.Runtime.Events;

/// <summary>Something happened to a room.</summary>
public abstract record RoomEvent(Room Room) : Event;