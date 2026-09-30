using Basil.Application.Common.Events;

namespace Basil.Application.Multiplayer.Events;

/// <summary>Something happened to a room.</summary>
public abstract record RoomEvent(Room Room) : Event;