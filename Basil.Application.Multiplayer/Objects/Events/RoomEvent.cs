using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Shared.Events;

namespace Basil.Application.Multiplayer.Objects.Events;

[EventLane(EventLane.Multiplayer)]
public abstract record RoomEvent(Room Room) : Event;