using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Mechanics;

namespace Basil.Application.Storage.Contracts.Multiplayer;

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
}

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