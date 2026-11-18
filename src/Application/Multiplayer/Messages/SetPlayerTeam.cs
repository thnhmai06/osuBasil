using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Mechanics;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record SetPlayerTeam(Connection Actor, Room Room, UserReference Player, GameTeam Team)
	: IRequest<RoomOutcome>;