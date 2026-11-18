using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Mediator;
using Basil.Domain.Multiplayer;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record RemoveReferee(Session Actor, Room Room, UserReference User) : IRequest<RoomOutcome>;