using Basil.Application.Multiplayer.Objects;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Multiplayer.Report.Matches;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record OpenRoom(
	Session Actor,
	string Name,
	string Password,
	RoomSource Source,
	bool IsPrivate,
	MatchSettings? Settings = null) : IRequest<OpenedRoom>;

public sealed record OpenedRoom(Room? Room, LobbyResult Result);