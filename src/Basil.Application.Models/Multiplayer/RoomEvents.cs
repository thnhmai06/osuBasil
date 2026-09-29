using Basil.Application.Models.Events;
using Basil.Application.Models.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Models.Multiplayer;

/// <summary>Something happened to a room.</summary>
public abstract record RoomEvent(Room Room) : Event;

/// <summary>A room's shared settings changed.</summary>
public abstract record RoomSettingsEvent(Room Room) : RoomEvent(Room);

/// <summary>The room's name changed.</summary>
public sealed record RoomNameChanged(Room Room, string Name) : RoomSettingsEvent(Room);

/// <summary>The room's public visibility changed.</summary>
public sealed record RoomVisibilityChanged(Room Room, bool IsVisible) : RoomSettingsEvent(Room);

/// <summary>The room's password changed.</summary>
public sealed record RoomPasswordChanged(Room Room, string Password) : RoomSettingsEvent(Room);

/// <summary>The room's selected beatmap changed.</summary>
public sealed record BeatmapChanged(Room Room, Beatmap? Beatmap) : RoomSettingsEvent(Room);

/// <summary>The room's game mode changed.</summary>
public sealed record GameModeChanged(Room Room, GameMode Mode) : RoomSettingsEvent(Room);

/// <summary>The room's global mods changed.</summary>
public sealed record ModsChanged(Room Room, GameMods Mods) : RoomSettingsEvent(Room);

/// <summary>The room's freemod setting changed.</summary>
public sealed record FreemodsChanged(Room Room, bool Enabled) : RoomSettingsEvent(Room);

/// <summary>The room's team arrangement changed, including any automatic team reassignments.</summary>
public sealed record TeamTypeChanged(Room Room, GameTeamType TeamType) : RoomSettingsEvent(Room);

/// <summary>The room's win condition changed.</summary>
public sealed record WinConditionChanged(Room Room, GameWinCondition WinCondition) : RoomSettingsEvent(Room);

/// <summary>A room's host or referee authority changed.</summary>
public abstract record RoomAuthorityEvent(Room Room) : RoomEvent(Room);

/// <summary>The room's host changed.</summary>
public sealed record HostChanged(Room Room, GameSession? Host) : RoomAuthorityEvent(Room);

/// <summary>A player was granted referee authority for the room.</summary>
public sealed record RefereeAdded(Room Room, User Referee) : RoomAuthorityEvent(Room);

/// <summary>A player's referee authority for the room was revoked.</summary>
public sealed record RefereeRemoved(Room Room, User Referee) : RoomAuthorityEvent(Room);

/// <summary>A player's access to a room — ban, unban, or invitation — changed.</summary>
public abstract record RoomAccessEvent(Room Room) : RoomEvent(Room);

/// <summary>A player was banned from the room, evicting them if they were seated.</summary>
public sealed record PlayerBanned(Room Room, User Player, RoomSlot? Vacated, GameSession? Evicted) : RoomAccessEvent(Room);

/// <summary>A player's ban from the room was lifted.</summary>
public sealed record PlayerUnbanned(Room Room, User Player) : RoomAccessEvent(Room);

/// <summary>A player was invited to the room.</summary>
public sealed record PlayerInvited(Room Room, User Player) : RoomAccessEvent(Room);

/// <summary>A player joined, left, or was removed from a room.</summary>
public abstract record RoomMembershipEvent(Room Room) : RoomEvent(Room);

/// <summary>A player joined the room and was assigned a slot.</summary>
public sealed record PlayerJoined(Room Room, GameSession Player, RoomSlot Slot) : RoomMembershipEvent(Room);

/// <summary>A player left the room, clearing their slot.</summary>
public sealed record PlayerLeft(Room Room, GameSession Player, RoomSlot Slot) : RoomMembershipEvent(Room);

/// <summary>A player was removed from the room.</summary>
public sealed record PlayerKicked(Room Room, GameSession Player, RoomSlot Slot) : RoomMembershipEvent(Room);

/// <summary>A player moved from one slot to another.</summary>
public sealed record PlayerMoved(Room Room, GameSession Player, RoomSlot From, RoomSlot To) : RoomMembershipEvent(Room);

/// <summary>A room's slots or their lock state changed.</summary>
public abstract record RoomSlotsEvent(Room Room) : RoomEvent(Room);

/// <summary>The room was resized to a new number of available slots.</summary>
public sealed record RoomResized(Room Room, int Size) : RoomSlotsEvent(Room);

/// <summary>The room's player-initiated slot lock (<c>!mp lock</c>) was toggled.</summary>
public sealed record RoomLockChanged(Room Room, bool Locked) : RoomSlotsEvent(Room);

/// <summary>Something happened to one of a room's slots.</summary>
public abstract record RoomSlotEvent(RoomSlot Slot) : RoomSlotsEvent(Slot.Slots.Room);

/// <summary>A slot's lock was toggled, evicting any occupant when locked.</summary>
public sealed record SlotLockChanged(RoomSlot Slot, bool Locked, GameSession? Evicted) : RoomSlotEvent(Slot);

/// <summary>A slot's assigned team changed.</summary>
public sealed record SlotTeamChanged(RoomSlot Slot, GameTeam? Team) : RoomSlotEvent(Slot);

/// <summary>A slot's selected mods changed.</summary>
public sealed record SlotModsChanged(RoomSlot Slot, GameMods? Mods) : RoomSlotEvent(Slot);

/// <summary>A slot's occupied-status changed.</summary>
public sealed record SlotStatusChanged(RoomSlot Slot, RoomSlotStatus? Status) : RoomSlotEvent(Slot);

/// <summary>Something happened during a room round.</summary>
public abstract record RoundEvent(Room Room, Round Round) : RoomEvent(Room);

/// <summary>A round started.</summary>
public sealed record RoundStarted(Room Room, Round Round) : RoundEvent(Room, Round);

/// <summary>The current round was aborted.</summary>
public sealed record RoundAborted(Room Room, Round Round) : RoundEvent(Room, Round);

/// <summary>The current round completed normally.</summary>
public sealed record RoundCompleted(Room Room, Round Round) : RoundEvent(Room, Round);

/// <summary>A player finished loading the current beatmap.</summary>
public sealed record PlayerLoaded(Room Room, Round Round, RoomSlot Slot) : RoundEvent(Room, Round);

/// <summary>Every playing player has finished loading the current beatmap.</summary>
public sealed record AllPlayersLoaded(Room Room, Round Round) : RoundEvent(Room, Round);

/// <summary>A player skipped the current beatmap's intro.</summary>
public sealed record PlayerSkipped(Room Room, Round Round, RoomSlot Slot) : RoundEvent(Room, Round);

/// <summary>Every playing player has skipped the current beatmap's intro.</summary>
public sealed record AllPlayersSkipped(Room Room, Round Round) : RoundEvent(Room, Round);

/// <summary>A player failed the current round.</summary>
public sealed record PlayerFailed(Room Room, Round Round, RoomSlot Slot) : RoundEvent(Room, Round);

/// <summary>A player finished the current round.</summary>
public sealed record PlayerCompleted(Room Room, Round Round, RoomSlot Slot) : RoundEvent(Room, Round);

/// <summary>The room closed and every seated player was evicted.</summary>
public sealed record RoomClosed(Room Room, IReadOnlyList<GameSession> Evicted) : RoomEvent(Room);
