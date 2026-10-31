using Basil.Application.Common.Events;
using Basil.Application.Multiplayer.Objects.Rooms;

namespace Basil.Application.Multiplayer.Objects.Events;

/// <summary>Something happened to a room.</summary>
public abstract record RoomEvent(Room Room) : Event;