using Basil.Application.Multiplayer.Objects;
using Basil.Application.Users;
using Basil.Application.Users.Objects.Sessions;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record UnbanPlayer(Session Actor, Room Room, int PlayerId) : IRequest<RoomTargetResult>;