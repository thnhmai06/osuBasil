using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer.Room;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Objects.Events;

public abstract record RoomLayoutEvent(Room Room) : RoomEvent(Room);

public sealed record RoomLockChanged(Room Room, bool Locked) : RoomLayoutEvent(Room);

public abstract record RoomSlotEvent(Room Room, int Slot) : RoomLayoutEvent(Room);

public sealed record RoomSlotLockChanged(
	Room Room,
	int Slot,
	bool Locked,
	StableSession? Evicted,
	StableSession? Host,
	RoomRoundProgress? RoundProgress)
	: RoomSlotEvent(Room, Slot);

public sealed record RoomSlotTeamChanged(Room Room, int Slot, GameTeam? Team) : RoomSlotEvent(Room, Slot);

public sealed record RoomSlotModsChanged(Room Room, int Slot, GameMods? Mods) : RoomSlotEvent(Room, Slot);

public sealed record RoomSlotStatusChanged(Room Room, int Slot, RoomSlotStatus? Status) : RoomSlotEvent(Room, Slot);

public sealed record RoomSlotsArranged(Room Room) : RoomLayoutEvent(Room);