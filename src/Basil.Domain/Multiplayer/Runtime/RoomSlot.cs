using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Runtime;

/// <summary>
///     Represents one of a match's 16 player slots.
///     The slot maintains the invariants between its availability, occupant, and state.
/// </summary>
public sealed class RoomSlot
{
	/// <summary>Gets the user occupying this slot, or null when the slot is not occupied.</summary>
	public User? User { get; private set; }

	/// <summary>Gets the availability of this slot.</summary>
	public RoomSlotAvailability Availability { get; private set; } = RoomSlotAvailability.Open;

	/// <summary>Gets the occupied-state of this slot, or null when the slot is not occupied.</summary>
	public RoomSlotStatus? Status { get; private set; }

	/// <summary>Gets the team assigned to the occupant.</summary>
	public GameTeam Team { get; private set; } = GameTeam.Neutral;

	/// <summary>Gets the mods assigned to the occupant.</summary>
	public GameMods GameMods { get; private set; } = GameMods.NoMod;

	/// <summary>Gets a value indicating whether the occupant has skipped the current beatmap's intro.</summary>
	public bool IntroSkipped { get; private set; }

	/// <summary>Gets a value indicating whether the occupant has finished loading the current beatmap.</summary>
	public bool BeatmapLoaded { get; private set; }

	/// <summary>
	///     Assigns a player to this slot, marking it occupied and resetting its player-specific state.
	/// </summary>
	/// <param name="player">The player to assign.</param>
	/// <exception cref="InvalidOperationException">The slot is not open.</exception>
	public void Assign(User player)
	{
		if (Availability != RoomSlotAvailability.Open)
			throw new InvalidOperationException("The slot is not open.");

		User = player;
		Availability = RoomSlotAvailability.Occupied;
		Status = RoomSlotStatus.NotReady;
		Team = GameTeam.Neutral;
		GameMods = GameMods.NoMod;
		IntroSkipped = false;
		BeatmapLoaded = false;
	}

	/// <summary>
	///     Changes the occupied-state of this slot.
	/// </summary>
	/// <param name="status">The new status to apply.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="status" /> is not a defined <see cref="RoomSlotStatus" /> value.
	/// </exception>
	/// <exception cref="InvalidOperationException">The slot is not occupied.</exception>
	public void SetStatus(RoomSlotStatus status)
	{
		if (!Enum.IsDefined(status))
			throw new ArgumentOutOfRangeException(nameof(status), status, null);

		EnsureOccupied();
		Status = status;
	}

	/// <summary>
	///     Locks the slot, vacating it and preventing anyone from joining until unlocked.
	/// </summary>
	public void Lock()
	{
		Clear();
		Availability = RoomSlotAvailability.Locked;
	}

	/// <summary>
	///     Unlocks the slot, allowing players to join it again.
	/// </summary>
	/// <remarks>Has no effect when the slot is not locked.</remarks>
	public void Unlock()
	{
		if (Availability == RoomSlotAvailability.Locked)
			Availability = RoomSlotAvailability.Open;
	}

	/// <summary>
	///     Changes the team assigned to the occupant.
	/// </summary>
	/// <param name="team">The team to assign.</param>
	/// <exception cref="InvalidOperationException">The slot is not occupied.</exception>
	public void SetTeam(GameTeam team)
	{
		EnsureOccupied();
		Team = team;
	}

	/// <summary>
	///     Changes the mods assigned to the occupant.
	/// </summary>
	/// <remarks>Speed-changing mods are always room-wide and are never applied per player.</remarks>
	/// <param name="gameMods">The mods to assign.</param>
	/// <exception cref="InvalidOperationException">The slot is not occupied.</exception>
	public void SetMods(GameMods gameMods)
	{
		EnsureOccupied();
		GameMods = gameMods & ~GameMods.SpeedChangingMods;
	}

	/// <summary>
	///     Marks the occupant as having skipped the current beatmap's intro.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	///     The slot is not occupied, or the occupant is not in the <see cref="RoomSlotStatus.Playing" />
	///     state.
	/// </exception>
	public void SkipIntro()
	{
		EnsureOccupied();

		if (Status is not RoomSlotStatus.Playing)
			throw new InvalidOperationException("The player is not playing.");

		IntroSkipped = true;
	}

	/// <summary>
	///     Marks the occupant as having finished loading the current beatmap.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	///     The slot is not occupied, or the occupant is not in the <see cref="RoomSlotStatus.Playing" />
	///     state.
	/// </exception>
	public void MarkLoaded()
	{
		EnsureOccupied();

		if (Status is not RoomSlotStatus.Playing)
			throw new InvalidOperationException("The player is not playing.");

		BeatmapLoaded = true;
	}

	/// <summary>
	///     Resets the per-round flags of the occupant, in preparation for a new round.
	/// </summary>
	internal void ResetRoundFlags()
	{
		BeatmapLoaded = false;
		IntroSkipped = false;
	}

	/// <summary>
	///     Moves the complete state of this slot to another slot.
	/// </summary>
	/// <param name="target">The slot to move the state to.</param>
	/// <exception cref="InvalidOperationException">
	///     This slot is not occupied, or <paramref name="target" /> is not open.
	/// </exception>
	public void MoveTo(RoomSlot target)
	{
		EnsureOccupied();
		target.Assign(User!);
		target.Status = Status;
		target.Team = Team;
		target.GameMods = GameMods;
		Clear();
	}

	/// <summary>
	///     Clears the slot's occupant and player-specific state.
	/// </summary>
	/// <remarks>Leaves the slot open, unless it was locked, in which case the lock is kept.</remarks>
	public void Clear()
	{
		User = null;
		if (Availability != RoomSlotAvailability.Locked)
			Availability = RoomSlotAvailability.Open;
		Status = null;
		Team = GameTeam.Neutral;
		GameMods = GameMods.NoMod;
		IntroSkipped = false;
		BeatmapLoaded = false;
	}

	private void EnsureOccupied()
	{
		if (Availability != RoomSlotAvailability.Occupied)
			throw new InvalidOperationException("The slot is not occupied.");
	}
}

/// <summary>
///     Describes the availability of a multiplayer match slot.
/// </summary>
public enum RoomSlotAvailability : byte
{
	/// <summary>The slot is empty and available for a player to join.</summary>
	Open,

	/// <summary>The slot is empty and unavailable; no one may join it.</summary>
	Locked,

	/// <summary>The slot is occupied by a player.</summary>
	Occupied
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