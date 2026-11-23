using Basil.Application.Sessions.Objects;
using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Objects.Events;

public abstract record RoomAccessEvent(Room Room) : RoomEvent(Room);

public sealed record RoomPlayerBanned(
	Room Room,
	UserRecord Actor,
	UserRecord Player,
	int? Vacated,
	StableSession? Evicted,
	StableSession? Host,
	RoomRoundProgress? RoundProgress)
	: RoomAccessEvent(Room);

public sealed record RoomPlayerUnbanned(Room Room, UserRecord Player) : RoomAccessEvent(Room);

public sealed record RoomPlayerInvited(Room Room, UserRecord Actor, UserRecord Player) : RoomAccessEvent(Room);