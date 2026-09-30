using Basil.Domain.Mechanics;
using Basil.Application.Sessions;

namespace Basil.Application.Multiplayer.Events;

/// <summary>A room's slots or their lock state changed.</summary>
public abstract record RoomSlotsEvent(Room Room) : RoomEvent(Room);

/// <summary>The room's player-initiated slot lock (<c>!mp lock</c>) was toggled.</summary>
public sealed record RoomLockChanged(Room Room, bool Locked) : RoomSlotsEvent(Room);

/// <summary>Something happened to one of a room's slots.</summary>
/// <param name="Room">The room the slot belongs to.</param>
/// <param name="Slot">The number of the slot, from 1 to 16.</param>
public abstract record RoomSlotEvent(Room Room, int Slot) : RoomSlotsEvent(Room);

/// <summary>A slot's lock was toggled, evicting any occupant when locked.</summary>
/// <param name="Room">The room the slot belongs to.</param>
/// <param name="Slot">The number of the slot, from 1 to 16.</param>
/// <param name="Locked">Whether the slot is locked after the change.</param>
/// <param name="Evicted">The connection that was removed, or <see langword="null" /> when no player was removed.</param>
/// <param name="Host">The room's host after the change.</param>
public sealed record SlotLockChanged(Room Room, int Slot, bool Locked, BanchoConnection? Evicted, BanchoConnection? Host)
	: RoomSlotEvent(Room, Slot);

/// <summary>A slot's assigned team changed.</summary>
/// <param name="Room">The room the slot belongs to.</param>
/// <param name="Slot">The number of the slot, from 1 to 16.</param>
/// <param name="Team">The team assigned after the change, or <see langword="null" /> when the slot has none.</param>
public sealed record SlotTeamChanged(Room Room, int Slot, GameTeam? Team) : RoomSlotEvent(Room, Slot);

/// <summary>A slot's selected mods changed.</summary>
/// <param name="Room">The room the slot belongs to.</param>
/// <param name="Slot">The number of the slot, from 1 to 16.</param>
/// <param name="Mods">The mods selected after the change, or <see langword="null" /> when the slot has none.</param>
public sealed record SlotModsChanged(Room Room, int Slot, GameMods? Mods) : RoomSlotEvent(Room, Slot);

/// <summary>A slot's occupied-status changed.</summary>
/// <param name="Room">The room the slot belongs to.</param>
/// <param name="Slot">The number of the slot, from 1 to 16.</param>
/// <param name="Status">The status after the change, or <see langword="null" /> when the slot is empty.</param>
public sealed record SlotStatusChanged(Room Room, int Slot, RoomSlotStatus? Status) : RoomSlotEvent(Room, Slot);
