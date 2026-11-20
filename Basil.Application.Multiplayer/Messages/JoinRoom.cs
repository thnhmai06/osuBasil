using Basil.Application.Multiplayer.Objects;
using Basil.Application.Users.Objects.Sessions;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record JoinRoom(Session Actor, Room Room, string Password) : IRequest<RoomJoinResult>;