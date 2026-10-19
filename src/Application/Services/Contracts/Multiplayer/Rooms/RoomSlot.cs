using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Mechanics;

namespace Basil.Application.Services.Contracts.Multiplayer.Rooms;

/// <summary>One of a room's 16 slots, holding its current occupant and per-slot settings.</summary>
public sealed class RoomSlot
{
	/// <summary>The number of this slot, from 1 to 16.</summary>
	public readonly int Index;

	/// <summary>The slot collection this slot belongs to.</summary>
	public readonly RoomSlots Slots;

	internal RoomSlot(RoomSlots slots, int index)
	{
		Slots = slots;
		Index = index;
	}

	/// <summary>Gets whether this slot is locked.</summary>
	public bool Locked { get; internal set; }

	/// <summary>Gets the connection of the player occupying this slot, or <see langword="null" /> when empty.</summary>
	public BanchoConnection? Player { get; internal set; }

	/// <summary>Gets the occupied-state of this slot.</summary>
	public RoomSlotStatus? Status { get; internal set; }

	/// <summary>Gets whether the occupant has skipped the intro of the current beatmap.</summary>
	public bool? IntroSkipped { get; internal set; }

	/// <summary>
	///     Gets whether the player has loaded the beatmap during the current round, or <see langword="null" /> when not
	///     playing.
	/// </summary>
	public bool? Loaded { get; internal set; }

	/// <summary>Gets the team assigned to this slot.</summary>
	public GameTeam? Team { get; internal set; }

	/// <summary>Gets the mods selected for this slot, used when free mods are enabled.</summary>
	public GameMods? Mods { get; internal set; }

	/// <summary>Moves this slot's player and state to another slot.</summary>
	/// <param name="target">The target slot to move to.</param>
	/// <remarks>The target slot must be open (not locked and empty). This slot becomes empty.</remarks>
	internal void MoveTo(RoomSlot target)
	{
		if (!ReferenceEquals(Slots, target.Slots))
			throw new ArgumentException("The target slot belongs to a different room.", nameof(target));
		if (Index == target.Index) return;
		if (Player is null) throw new InvalidOperationException("The slot has no player.");
		if (target.Locked || target.Player is not null)
			throw new InvalidOperationException("The target slot is not open.");

		target.Restore(Capture());
		Clear();
	}

	/// <summary>Captures the current state of this slot.</summary>
	/// <returns>The captured state.</returns>
	internal RoomSlotState Capture()
	{
		return new RoomSlotState(Player!, Status, Team, Mods, IntroSkipped, Loaded);
	}

	/// <summary>Restores a captured state into this slot.</summary>
	/// <param name="state">The state to restore.</param>
	internal void Restore(RoomSlotState state)
	{
		Player = state.Player;
		Status = state.Status;
		Team = state.Team;
		Mods = state.Mods;
		IntroSkipped = state.IntroSkipped;
		Loaded = state.Loaded;
	}

	/// <summary>Clears this slot, removing all player state.</summary>
	internal void Clear()
	{
		Player = null;
		Status = null;
		Team = null;
		Mods = null;
		IntroSkipped = null;
		Loaded = null;
	}

	/// <summary>Sets the status of this slot.</summary>
	/// <param name="status">The new status, or null to clear.</param>
	internal void SetStatus(RoomSlotStatus? status)
	{
		if (Status == status) return;

		Status = status;
		IntroSkipped = status == RoomSlotStatus.Playing ? false : null;
		Loaded = status == RoomSlotStatus.Playing ? false : null;
	}
}

/// <summary>Represents the captured state of a room slot.</summary>
/// <param name="Player">The player in the slot.</param>
/// <param name="Status">The slot status.</param>
/// <param name="Team">The team, if any.</param>
/// <param name="Mods">The mods, if any.</param>
/// <param name="IntroSkipped">Whether intro was skipped.</param>
/// <param name="Loaded">Whether the beatmap was loaded.</param>
internal readonly record struct RoomSlotState(
	BanchoConnection Player,
	RoomSlotStatus? Status,
	GameTeam? Team,
	GameMods? Mods,
	bool? IntroSkipped,
	bool? Loaded);

/// <summary>
///     Describes the occupied-state of a player in a multiplayer match slot.
/// </summary>
public enum RoomSlotStatus : byte
{
	/// <summary>The player does not have the current beatmap.</summary>
	NoMap,

	/// <summary>The player has the current beatmap but is not ready.</summary>
	NotReady,

	/// <summary>The player is ready to play.</summary>
	Ready,

	/// <summary>The player is currently playing the beatmap.</summary>
	Playing,

	/// <summary>The player has finished the beatmap and is waiting for the round to end.</summary>
	Complete
}