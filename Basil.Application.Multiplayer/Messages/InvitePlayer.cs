using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Sessions;
using Basil.Application.Multiplayer.Objects;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record InvitePlayer(Session Actor, Room Room, User Target) : IRequest<RoomInviteResult>;