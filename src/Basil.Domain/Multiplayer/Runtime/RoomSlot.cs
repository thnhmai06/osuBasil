using Basil.Domain.Multiplayer.Records;
using Basil.Domain.Scores;
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
	public MatchTeam Team { get; private set; } = MatchTeam.Neutral;

	/// <summary>Gets the mods assigned to the occupant.</summary>
	public Mods Mods { get; private set; } = Mods.NoMod;

	/// <summary>Gets a value indicating whether the occupant has skipped the current beatmap's intro.</summary>
	public bool IntroSkipped { get; private set; }

	/// <summary>Gets a value indicating whether the slot is empty.</summary>
	public bool IsEmpty => User is null;

	/// <summary>
	///     Assigns a player to this slot and resets its player-specific state.
	/// </summary>
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
		Team = MatchTeam.Neutral;
		Mods = Mods.NoMod;
		IntroSkipped = false;
	}

	/// <summary>
	///     Changes the slot's status and automatically adjusts related state
	///     to maintain the slot's invariants.
	/// </summary>
	public void SetStatus(RoomSlotStatus status)
	{
		switch (status)
		{
			case RoomSlotStatus.Open:
			case RoomSlotStatus.Locked:
				User = null;
				Status = status;
				Team = MatchTeam.Neutral;
				Mods = Mods.NoMod;
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
	public void SetTeam(MatchTeam team)
	{
		EnsureOccupied();
		Team = team;
	}

	/// <summary>
	///     Changes the mods assigned to the occupant.
	/// </summary>
	public void SetMods(Mods mods)
	{
		EnsureOccupied();
		Mods = mods;
	}

	/// <summary>
	///     Marks the occupant as having skipped the current beatmap's intro.
	/// </summary>
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
		Mods = other.Mods;
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