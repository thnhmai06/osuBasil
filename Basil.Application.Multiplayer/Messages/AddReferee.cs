using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record AddReferee(Session Actor, Room Room, UserReference User) : IRequest<RoomOutcome>;