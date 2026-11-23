using Basil.Application.Sessions.Objects;
using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Objects.Events;

public abstract record RoomMembershipEvent(Room Room) : RoomEvent(Room);

public sealed record RoomPlayerJoined(
	Room Room,
	StableSession Player,
	int Slot,
	StableSession? Replaced,
	RoomRoundProgress? RoundProgress)
	: RoomMembershipEvent(Room);

public sealed record RoomPlayerLeft(
	Room Room,
	StableSession Player,
	int Slot,
	StableSession? Host,
	RoomRoundProgress? RoundProgress)
	: RoomMembershipEvent(Room);

public sealed record RoomPlayerKicked(
	Room Room,
	UserRecord Actor,
	StableSession Player,
	int Slot,
	StableSession? Host,
	RoomRoundProgress? RoundProgress)
	: RoomMembershipEvent(Room);

public sealed record RoomPlayerMoved(Room Room, StableSession Player, int From, int To)
	: RoomMembershipEvent(Room);

public sealed record RoomObserverJoined(Room Room, TourneySession Observer) : RoomMembershipEvent(Room);

public sealed record RoomObserverLeft(Room Room, TourneySession Observer) : RoomMembershipEvent(Room);