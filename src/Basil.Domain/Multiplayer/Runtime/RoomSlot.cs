using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Runtime;

/// <summary>
///     Represents one of a match's 16 player slots.
///     The slot maintains the invariants between its occupant and state.
/// </summary>
public sealed class RoomSlot
{
	/// <summary>Gets the user occupying this slot, or null when the slot is empty.</summary>
	public User? User { get; private set; }

	/// <summary>Gets the current state of this slot.</summary>
	public RoomSlotStatus Status { get; private set; } = RoomSlotStatus.Open;

	/// <summary>Gets the team assigned to the occupant.</summary>
	public GameTeam Team { get; private set; } = GameTeam.Neutral;

	/// <summary>Gets the mods assigned to the occupant.</summary>
	public GameMods GameMods { get; private set; } = GameMods.NoMod;

	/// <summary>Gets a value indicating whether the occupant has skipped the current beatmap's intro.</summary>
	public bool IntroSkipped { get; private set; }

	/// <summary>Gets a value indicating whether the slot is empty.</summary>
	public bool IsEmpty => User is null;

	/// <summary>
	///     Assigns a player to this slot and resets its player-specific state.
	/// </summary>
	/// <remarks>
	///     Passing <see langword="null" /> vacates the slot, leaving it open — or locked, when the
	///     slot was locked. Assigning a player marks the slot not ready and resets the team, mods,
	///     and intro state.
	/// </remarks>
	/// <param name="player">The player to assign, or <see langword="null" /> to vacate the slot.</param>
	public void Assign(User? player)
	{
		if (player is null)
		{
			SetStatus(Status == RoomSlotStatus.Locked
				? RoomSlotStatus.Locked
				: RoomSlotStatus.Open);
			return;
		}

		User = player;
		Status = RoomSlotStatus.NotReady;
		Team = GameTeam.Neutral;
		GameMods = GameMods.NoMod;
		IntroSkipped = false;
	}

	/// <summary>
	///     Changes the slot's status and automatically adjusts related state
	///     to maintain the slot's invariants.
	/// </summary>
	/// <remarks>
	///     <see cref="RoomSlotStatus.Open" /> and <see cref="RoomSlotStatus.Locked" /> vacate the
	///     slot and reset its team, mods, and intro state. The occupied statuses
	///     (<see cref="RoomSlotStatus.NoMap" />, <see cref="RoomSlotStatus.NotReady" />,
	///     <see cref="RoomSlotStatus.Ready" />, and <see cref="RoomSlotStatus.Playing" />) require
	///     an occupant and leave the player-specific state untouched apart from the intro flag.
	/// </remarks>
	/// <param name="status">The new status to apply.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="status" /> is not a defined <see cref="RoomSlotStatus" /> value.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	///     <paramref name="status" /> is an occupied status but the slot is empty.
	/// </exception>
	public void SetStatus(RoomSlotStatus status)
	{
		switch (status)
		{
			case RoomSlotStatus.Open:
			case RoomSlotStatus.Locked:
				User = null;
				Status = status;
				Team = GameTeam.Neutral;
				GameMods = GameMods.NoMod;
				break;

			case RoomSlotStatus.NoMap:
			case RoomSlotStatus.NotReady:
			case RoomSlotStatus.Ready:
			case RoomSlotStatus.Playing:
				EnsureOccupied();

				Status = status;
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(status), status, null);
		}

		IntroSkipped = false;
	}

	/// <summary>
	///     Changes the team assigned to the occupant.
	/// </summary>
	/// <param name="team">The team to assign.</param>
	/// <exception cref="InvalidOperationException">The slot is empty.</exception>
	public void SetTeam(GameTeam team)
	{
		EnsureOccupied();
		Team = team;
	}

	/// <summary>
	///     Changes the mods assigned to the occupant.
	/// </summary>
	/// <param name="gameMods">The mods to assign.</param>
	/// <exception cref="InvalidOperationException">The slot is empty.</exception>
	public void SetMods(GameMods gameMods)
	{
		EnsureOccupied();
		GameMods = gameMods;
	}

	/// <summary>
	///     Marks the occupant as having skipped the current beatmap's intro.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	///     The slot is empty, or the occupant is not in the <see cref="RoomSlotStatus.Playing" />
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
	///     Copies the complete state from another slot, or clears this slot when
	///     <paramref name="other" /> is null.
	/// </summary>
	/// <param name="other">The slot to copy from, or <see langword="null" /> to clear this slot.</param>
	public void CopyFrom(RoomSlot? other)
	{
		if (other is null)
		{
			Clear();
			return;
		}

		User = other.User;
		Status = other.Status;
		Team = other.Team;
		GameMods = other.GameMods;
		IntroSkipped = other.IntroSkipped;
	}

	/// <summary>
	///     Clears the slot and restores its default open state.
	/// </summary>
	public void Clear()
	{
		SetStatus(RoomSlotStatus.Open);
	}

	private void EnsureOccupied()
	{
		if (User is null)
			throw new InvalidOperationException("The slot is empty.");
	}
}

/// <summary>
///     Describes the state of a single player slot in a multiplayer match.
/// </summary>
public enum RoomSlotStatus : byte
{
	/// <summary>The slot is empty and available.</summary>
	Open,

	/// <summary>The slot is empty and unavailable.</summary>
	Locked,

	/// <summary>The player does not have the current beatmap.</summary>
	NoMap,

	/// <summary>The player has the current beatmap but is not ready.</summary>
	NotReady,

	/// <summary>The player is ready to play.</summary>
	Ready,

	/// <summary>The player is currently playing the beatmap.</summary>
	Playing
}