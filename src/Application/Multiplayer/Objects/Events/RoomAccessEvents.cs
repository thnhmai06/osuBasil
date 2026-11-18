using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Events;

public abstract record RoomAccessEvent(Room Room) : RoomEvent(Room);

public sealed record RoomPlayerBanned(
	Room Room,
	UserRecord Actor,
	UserRecord Player,
	int? Vacated,
	StableConnection? Evicted,
	StableConnection? Host,
	RoomRoundProgress? RoundProgress)
	: RoomAccessEvent(Room);

public sealed record RoomPlayerUnbanned(Room Room, UserRecord Player) : RoomAccessEvent(Room);

public sealed record RoomPlayerInvited(Room Room, UserRecord Actor, UserRecord Player) : RoomAccessEvent(Room);