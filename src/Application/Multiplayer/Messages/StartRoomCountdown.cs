using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record StartRoomCountdown(Connection Actor, Room Room, TimeSpan Duration, bool StartsRound)
	: IRequest<RoomOutcome>;