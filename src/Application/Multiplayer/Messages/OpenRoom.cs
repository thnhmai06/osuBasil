using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record OpenRoom(
	Connection Actor,
	string Name,
	string Password,
	RoomSource Source,
	bool IsPrivate,
	MatchSettings? Settings = null) : IRequest<OpenedRoom>;

public sealed record OpenedRoom(Room? Room, RoomOutcome Result);