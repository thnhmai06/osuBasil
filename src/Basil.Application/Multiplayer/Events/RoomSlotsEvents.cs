using Basil.Domain.Mechanics;
using Basil.Application.Sessions;

namespace Basil.Application.Multiplayer.Events;

/// <summary>A room's slots or their lock state changed.</summary>
public abstract record RoomSlotsEvent(Room Room) : RoomEvent(Room);

/// <summary>The room was resized to a new number of available slots.</summary>
public sealed record RoomResized(Room Room, int Size) : RoomSlotsEvent(Room);

/// <summary>The room's player-initiated slot lock (<c>!mp lock</c>) was toggled.</summary>
public sealed record RoomLockChanged(Room Room, bool Locked) : RoomSlotsEvent(Room);

/// <summary>Something happened to one of a room's slots.</summary>
public abstract record RoomSlotEvent(RoomSlot Slot) : RoomSlotsEvent(Slot.Slots.Room);

/// <summary>A slot's lock was toggled, evicting any occupant when locked.</summary>
public sealed record SlotLockChanged(RoomSlot Slot, bool Locked, BanchoConnection? Evicted) : RoomSlotEvent(Slot);

/// <summary>A slot's assigned team changed.</summary>
public sealed record SlotTeamChanged(RoomSlot Slot, GameTeam? Team) : RoomSlotEvent(Slot);

/// <summary>A slot's selected mods changed.</summary>
public sealed record SlotModsChanged(RoomSlot Slot, GameMods? Mods) : RoomSlotEvent(Slot);

/// <summary>A slot's occupied-status changed.</summary>
public sealed record SlotStatusChanged(RoomSlot Slot, RoomSlotStatus? Status) : RoomSlotEvent(Slot);