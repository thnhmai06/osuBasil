using Basil.Application.Models.Multiplayer;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Models.Events.Multiplayer;

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