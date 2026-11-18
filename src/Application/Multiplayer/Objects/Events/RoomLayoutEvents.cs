using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Mechanics;

namespace Basil.Application.Multiplayer.Objects.Events;

public abstract record RoomLayoutEvent(Room Room) : RoomEvent(Room);

public sealed record RoomLockChanged(Room Room, bool Locked) : RoomLayoutEvent(Room);

public abstract record RoomSlotEvent(Room Room, int Slot) : RoomLayoutEvent(Room);

public sealed record RoomSlotLockChanged(
	Room Room,
	int Slot,
	bool Locked,
	StableConnection? Evicted,
	StableConnection? Host,
	RoomRoundProgress? RoundProgress)
	: RoomSlotEvent(Room, Slot);

public sealed record RoomSlotTeamChanged(Room Room, int Slot, GameTeam? Team) : RoomSlotEvent(Room, Slot);

public sealed record RoomSlotModsChanged(Room Room, int Slot, GameMods? Mods) : RoomSlotEvent(Room, Slot);

public sealed record RoomSlotStatusChanged(Room Room, int Slot, RoomSlotStatus? Status) : RoomSlotEvent(Room, Slot);

public sealed record RoomSlotsArranged(Room Room) : RoomLayoutEvent(Room);