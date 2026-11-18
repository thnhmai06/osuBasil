using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record InvitePlayer(Session Actor, Room Room, User Target) : IRequest<RoomOutcome>;