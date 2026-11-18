using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Sessions;
using Mediator;
using Basil.Domain.Multiplayer;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record StartRoomCountdown(Session Actor, Room Room, TimeSpan Duration, bool StartsRound)
	: IRequest<RoomOutcome>;