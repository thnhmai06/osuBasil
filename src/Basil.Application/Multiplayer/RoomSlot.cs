using Basil.Application.Multiplayer.Events;
using Basil.Application.Sessions;
using Basil.Domain.Mechanics;

namespace Basil.Application.Multiplayer;

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
	public bool Locked { get; private set; }

	/// <summary>Gets the connection of the player occupying this slot, or <see langword="null" /> when empty.</summary>
	public BanchoConnection? Player { get; private set; }

	/// <summary>Gets the occupied-state of this slot.</summary>
	public RoomSlotStatus? Status { get; private set; }

	/// <summary>Gets whether the occupant has skipped the intro of the current beatmap.</summary>
	public bool? IntroSkipped { get; private set; }

	/// <summary>
	///     Gets whether the player has loaded the beatmap during the current round, or <see langword="null" /> when not
	///     playing.
	/// </summary>
	public bool? Loaded { get; private set; }

	/// <summary>Gets the team assigned to this slot.</summary>
	public GameTeam? Team { get; private set; }

	/// <summary>Gets the mods selected for this slot, used when free mods are enabled.</summary>
	public GameMods? Mods { get; private set; }

	/// <summary>Seats <paramref name="player" /> in this slot.</summary>
	/// <param name="player">The connection to seat.</param>
	/// <exception cref="InvalidOperationException">The slot is locked or already occupied.</exception>
	internal void Occupy(BanchoConnection player)
	{
		if (Locked) throw new InvalidOperationException("The slot is locked.");
		if (Player is not null) throw new InvalidOperationException("The slot is already occupied.");

		Player = player;
		Status = RoomSlotStatus.NotReady;
		Mods = GameMods.NoMod;
	}

	/// <summary>Clears this slot, making it empty.</summary>
	internal void Clear()
	{
		Player = null;
		Status = null;
		Team = null;
		Mods = null;
		IntroSkipped = null;
		Loaded = null;
	}

	/// <summary>Sets the slot's own mods without validating the room's freemod setting or playing state.</summary>
	/// <param name="mods">The mods to set, or <see langword="null" /> to clear them.</param>
	internal void SetMods(GameMods? mods)
	{
		Mods = mods;
	}

	/// <summary>Assigns a team without validating the room's team type or playing state.</summary>
	/// <param name="team">The team to assign, or <see langword="null" /> to clear it.</param>
	internal void SetTeam(GameTeam? team)
	{
		Team = team;
	}

	/// <summary>Assigns a status without emitting a <see cref="SlotStatusChanged" /> event.</summary>
	/// <param name="status">The status to assign, or <see langword="null" /> to clear it.</param>
	internal void SetStatus(RoomSlotStatus? status)
	{
		if (Status == status) return;
		Status = status;
		IntroSkipped = status is RoomSlotStatus.Playing ? false : null;
		Loaded = status is RoomSlotStatus.Playing ? false : null;
	}

	/// <summary>Locks or unlocks the slot without emitting a <see cref="SlotLockChanged" /> event.</summary>
	/// <param name="locked">The lock state to assign.</param>
	internal void SetLocked(bool locked)
	{
		Locked = locked;
	}

	/// <summary>Sets whether the occupant has skipped the intro of the current beatmap.</summary>
	/// <param name="skipped">The skip state to assign.</param>
	internal void SetIntroSkipped(bool? skipped)
	{
		IntroSkipped = skipped;
	}

	/// <summary>Sets whether the occupant has loaded the beatmap during the current round.</summary>
	/// <param name="loaded">The load state to assign.</param>
	internal void SetLoaded(bool? loaded)
	{
		Loaded = loaded;
	}

	internal void MoveTo(RoomSlot target)
	{
		if (!ReferenceEquals(Slots, target.Slots))
			throw new ArgumentException("The target slot belongs to a different room.", nameof(target));
		if (Index == target.Index) return;
		if (Player is null) throw new InvalidOperationException("The slot has no player.");
		if (target.Locked || target.Player is not null)
			throw new InvalidOperationException("The target slot is not open.");

		var player = Player;
		target.Player = player;
		target.Status = Status;
		target.Team = Team;
		target.Mods = Mods;
		target.IntroSkipped = IntroSkipped;
		target.Loaded = Loaded;

		Player = null;
		Status = null;
		Team = null;
		Mods = null;
		IntroSkipped = null;
		Loaded = null;
	}
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