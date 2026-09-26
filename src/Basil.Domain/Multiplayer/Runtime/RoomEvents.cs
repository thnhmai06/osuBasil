using Basil.Domain.Events;
using Basil.Domain.Multiplayer.Records;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Runtime;

/// <summary>A player joined the room and was assigned a slot.</summary>
public sealed record PlayerJoined(Room Room, User Player, int Slot) : IDomainEvent;

/// <summary>A player left the room, clearing their slot.</summary>
public sealed record PlayerLeft(Room Room, User Player) : IDomainEvent;

/// <summary>The room's host changed.</summary>
public sealed record HostChanged(Room Room, User? Host) : IDomainEvent;

/// <summary>A slot's team, mods, or occupied-status changed.</summary>
public sealed record SlotChanged(Room Room, int Slot) : IDomainEvent;

/// <summary>A slot was locked, evicting its occupant if any.</summary>
public sealed record SlotLocked(Room Room, int Slot, User? Evicted) : IDomainEvent;

/// <summary>The room's settings — beatmap, mode, mods, team arrangement, name, or password — changed.</summary>
public sealed record SettingsChanged(Room Room) : IDomainEvent;

/// <summary>The room's player-initiated slot and team lock (<c>!mp lock</c>) was toggled.</summary>
public sealed record RoomLockChanged(Room Room, bool Locked) : IDomainEvent;

/// <summary>A player was granted referee authority for the room.</summary>
public sealed record RefereeAdded(Room Room, User Referee) : IDomainEvent;

/// <summary>A player's referee authority for the room was revoked.</summary>
public sealed record RefereeRemoved(Room Room, User Referee) : IDomainEvent;

/// <summary>A player was banned from the room.</summary>
public sealed record PlayerBanned(Room Room, User Player) : IDomainEvent;

/// <summary>A player's ban from the room was lifted.</summary>
public sealed record PlayerUnbanned(Room Room, User Player) : IDomainEvent;

/// <summary>A player was removed from the room.</summary>
public sealed record PlayerKicked(Room Room, User Player) : IDomainEvent;

/// <summary>A player was invited to the room.</summary>
public sealed record PlayerInvited(Room Room, User Player) : IDomainEvent;

/// <summary>A round started.</summary>
public sealed record RoundStarted(Room Room) : IDomainEvent;

/// <summary>A player finished loading the current beatmap.</summary>
public sealed record PlayerLoaded(Room Room, int Slot) : IDomainEvent;

/// <summary>Every playing player has finished loading the current beatmap.</summary>
public sealed record AllPlayersLoaded(Room Room) : IDomainEvent;

/// <summary>A player skipped the current beatmap's intro.</summary>
public sealed record PlayerSkipped(Room Room, int Slot) : IDomainEvent;

/// <summary>Every playing player has skipped the current beatmap's intro.</summary>
public sealed record AllPlayersSkipped(Room Room) : IDomainEvent;

/// <summary>A player failed the current round.</summary>
public sealed record PlayerFailed(Room Room, int Slot) : IDomainEvent;

/// <summary>A player finished the current round.</summary>
public sealed record PlayerCompleted(Room Room, int Slot) : IDomainEvent;

/// <summary>The current round ended, either because every player finished or because it was aborted.</summary>
/// <param name="Room">The room whose round ended.</param>
/// <param name="Aborted"><see langword="true" /> if the round was aborted.</param>
/// <param name="Round">The record of the round that ended, or <see langword="null" /> when none was tracked.</param>
public sealed record RoundEnded(Room Room, bool Aborted, Round? Round) : IDomainEvent;