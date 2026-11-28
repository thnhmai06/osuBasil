using Basil.Application.Sessions.Objects;
using Basil.Domain.Multiplayer.Room;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record ToggleSlotLock(Session Actor, Room Room, int Index) : IRequest<RoomActionResult>;