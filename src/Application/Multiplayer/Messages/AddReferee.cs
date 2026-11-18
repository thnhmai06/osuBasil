using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record AddReferee(Session Actor, Room Room, UserReference User) : IRequest<RoomOutcome>;