using Basil.Application.Users;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer.Room;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record UnbanPlayer(Session Actor, Room Room, UserReference Player) : IRequest<RoomActionResult>;