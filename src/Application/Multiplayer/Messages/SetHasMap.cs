using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Sessions;
using Mediator;
using Basil.Domain.Multiplayer;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record SetHasMap(Session Actor, Room Room, bool Has) : IRequest<RoomOutcome>;