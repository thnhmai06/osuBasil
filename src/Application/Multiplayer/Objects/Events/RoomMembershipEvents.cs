using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Events;

public abstract record RoomMembershipEvent(Room Room) : RoomEvent(Room);

public sealed record RoomPlayerJoined(
	Room Room,
	StableConnection Player,
	int Slot,
	StableConnection? Replaced,
	RoomRoundProgress? RoundProgress)
	: RoomMembershipEvent(Room);

public sealed record RoomPlayerLeft(
	Room Room,
	StableConnection Player,
	int Slot,
	StableConnection? Host,
	RoomRoundProgress? RoundProgress)
	: RoomMembershipEvent(Room);

public sealed record RoomPlayerKicked(
	Room Room,
	UserRecord Actor,
	StableConnection Player,
	int Slot,
	StableConnection? Host,
	RoomRoundProgress? RoundProgress)
	: RoomMembershipEvent(Room);

public sealed record RoomPlayerMoved(Room Room, StableConnection Player, int From, int To)
	: RoomMembershipEvent(Room);

public sealed record RoomObserverJoined(Room Room, TourneyConnection Observer) : RoomMembershipEvent(Room);

public sealed record RoomObserverLeft(Room Room, TourneyConnection Observer) : RoomMembershipEvent(Room);