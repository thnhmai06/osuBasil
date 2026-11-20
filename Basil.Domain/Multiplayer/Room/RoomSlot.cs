using Basil.Domain.Game;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Room;

/// <summary>One numbered place in a room that a player can occupy.</summary>
public sealed class RoomSlot
{
	/// <summary>Gets the 1-based number of the slot.</summary>
	public int Index { get; }

	/// <summary>The slots of the room this slot belongs to.</summary>
	internal readonly RoomSlots Slots;

	/// <summary>Creates an empty slot.</summary>
	/// <param name="slots">The slots of the room the slot belongs to.</param>
	/// <param name="index">The 1-based number of the slot.</param>
	internal RoomSlot(RoomSlots slots, int index)
	{
		Slots = slots;
		Index = index;
	}

	/// <summary>Gets whether the slot is closed to new players.</summary>
	public bool Locked { get; internal set; }

	/// <summary>Gets the player in the slot, or <see langword="null"/> when it is empty.</summary>
	public UserRecord? Player { get; internal set; }

	/// <summary>Gets the status of the player in the slot, or <see langword="null"/> when it is empty.</summary>
	public RoomSlotStatus? Status { get; internal set; }

	/// <summary>Gets whether the player has skipped the intro of the round; <see langword="null"/> unless the player is playing.</summary>
	public bool? IntroSkipped { get; internal set; }

	/// <summary>Gets whether the player has loaded the round's beatmap; <see langword="null"/> unless the player is playing.</summary>
	public bool? Loaded { get; internal set; }

	/// <summary>Gets the player's team, or <see langword="null"/> when the room is not in a team mode or the slot is empty.</summary>
	public GameTeam? Team { get; internal set; }

	/// <summary>Gets the mods the player chose for themselves under freemods, or <see langword="null"/> when the slot is empty.</summary>
	public GameMods? Mods { get; internal set; }

	/// <summary>Seats a player in the slot as not ready and without mods.</summary>
	/// <param name="player">The player to seat.</param>
	/// <exception cref="InvalidOperationException">The slot is locked or already occupied.</exception>
	internal void Occupy(UserRecord player)
	{
		if (Locked) throw new InvalidOperationException("The slot is locked.");
		if (Player is not null) throw new InvalidOperationException("The slot is already occupied.");

		Player = player;
		Status = RoomSlotStatus.NotReady;
		Mods = GameMods.NoMod;
	}

	/// <summary>Moves the player, with status, team and mods, to another open slot of the same room and empties this one.</summary>
	/// <param name="target">The slot to move to.</param>
	/// <exception cref="ArgumentException">The target belongs to another room.</exception>
	/// <exception cref="InvalidOperationException">The slot has no player, or the target is locked or taken.</exception>
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

	/// <summary>Gets the player and everything the slot keeps about them.</summary>
	internal RoomSlotState Capture()
	{
		return new RoomSlotState(Player!, Status, Team, Mods, IntroSkipped, Loaded);
	}

	/// <summary>Puts a captured player and their state into the slot.</summary>
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

	/// <summary>Empties the slot of its player and their state; the lock is kept.</summary>
	internal void Clear()
	{
		Player = null;
		Status = null;
		Team = null;
		Mods = null;
		IntroSkipped = null;
		Loaded = null;
	}

	/// <summary>Sets the status of the player; entering playing resets the loaded and intro-skipped flags to false, any other status clears them.</summary>
	/// <param name="status">The new status.</param>
	internal void SetStatus(RoomSlotStatus? status)
	{
		if (Status == status) return;

		Status = status;
		IntroSkipped = status == RoomSlotStatus.Playing ? false : null;
		Loaded = status == RoomSlotStatus.Playing ? false : null;
	}
}

/// <summary>A player together with everything a slot keeps about them.</summary>
/// <param name="Player">The player.</param>
/// <param name="Status">The player's status.</param>
/// <param name="Team">The player's team.</param>
/// <param name="Mods">The player's mods.</param>
/// <param name="IntroSkipped">Whether the player skipped the intro.</param>
/// <param name="Loaded">Whether the player loaded the beatmap.</param>
internal readonly record struct RoomSlotState(
	UserRecord Player,
	RoomSlotStatus? Status,
	GameTeam? Team,
	GameMods? Mods,
	bool? IntroSkipped,
	bool? Loaded);

/// <summary>The state of the player in a slot.</summary>
public enum RoomSlotStatus : byte
{
	/// <summary>The player does not have the room's beatmap.</summary>
	NoMap,

	/// <summary>The player has the beatmap and is not ready.</summary>
	NotReady,

	/// <summary>The player is ready to start.</summary>
	Ready,

	/// <summary>The player is playing the round in progress.</summary>
	Playing,

	/// <summary>The player has finished the round in progress.</summary>
	Complete
}