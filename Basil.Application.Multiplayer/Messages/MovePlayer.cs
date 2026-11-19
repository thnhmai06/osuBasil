using Basil.Application.Users;
using Basil.Application.Users.Objects.Sessions;
using Basil.Application.Multiplayer.Objects;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record MovePlayer(Session Actor, Room Room, UserReference Player, int Index)
	: IRequest<LobbyResult>;