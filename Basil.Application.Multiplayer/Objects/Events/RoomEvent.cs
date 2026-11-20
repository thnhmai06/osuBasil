using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Shared.Events;

namespace Basil.Application.Multiplayer.Objects.Events;

public abstract record RoomEvent(Room Room) : Event;