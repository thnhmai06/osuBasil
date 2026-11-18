using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Game.Mechanics;
using Mediator;
using Basil.Domain.Multiplayer;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record SetPlayerTeam(Session Actor, Room Room, UserReference Player, GameTeam Team)
	: IRequest<RoomOutcome>;