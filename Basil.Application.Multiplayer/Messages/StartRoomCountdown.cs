using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer.Room;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record StartRoomCountdown(Session Actor, Room Room, TimeSpan Duration, bool StartsRound)
	: IRequest<RoomActionResult>;