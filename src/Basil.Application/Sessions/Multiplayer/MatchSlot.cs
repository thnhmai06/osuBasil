using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;

namespace Basil.Application.Sessions.Multiplayer;

/// <summary>
///     Represents one of a match's 16 slots, holding the occupant's id, status, team, and mods for
///     the current round. A plain mutable holder: synchronization is the owning
///     <see cref="MatchSession" />'s responsibility (its <see cref="MatchSession.Lock" />), not this
///     type's.
/// </summary>
public sealed class MatchSlot
{
	/// <summary>Gets or sets the id of the player occupying this slot, or null when the slot is empty.</summary>
	public int? PlayerId { get; set; }

	/// <summary>
	///     Gets or sets the slot's status, which is open by default and indicates whether the slot is occupied, locked,
	///     ready, or not ready.
	/// </summary>
	public SlotStatus Status { get; set; } = SlotStatus.Open;

	/// <summary>
	///     Gets or sets the team this slot's occupant is on, neutral when the room is not team-based or the slot is
	///     empty.
	/// </summary>
	public MatchTeam Team { get; set; } = MatchTeam.Neutral;

	/// <summary>Gets or sets the mods applied to this slot, meaningful when freemod mode is on.</summary>
	public Mods Mods { get; set; } = Mods.NoMod;

	/// <summary>Gets or sets a value that indicates whether the occupant has loaded into the current beatmap.</summary>
	public bool Loaded { get; set; }

	/// <summary>Gets or sets a value that indicates whether the occupant has skipped the current beatmap's intro.</summary>
	public bool Skipped { get; set; }

	/// <summary>Gets a value that indicates whether no player occupies this slot.</summary>
	public bool Empty => PlayerId is null;

	/// <summary>
	///     Copies the occupant, status, team, and mods from <paramref name="other" /> into this
	///     slot, leaving the loaded and skipped flags untouched.
	/// </summary>
	/// <param name="other">The slot to copy from.</param>
	public void CopyFrom(MatchSlot other)
	{
		PlayerId = other.PlayerId;
		Status = other.Status;
		Team = other.Team;
		Mods = other.Mods;
	}

	/// <summary>
	///     Clears the slot back to empty with the given status, resetting the team, mods, loaded,
	///     and skipped state.
	/// </summary>
	/// <param name="newStatus">The status to give the cleared slot, defaulting to open.</param>
	public void Reset(SlotStatus newStatus = SlotStatus.Open)
	{
		PlayerId = null;
		Status = newStatus;
		Team = MatchTeam.Neutral;
		Mods = Mods.NoMod;
		Loaded = false;
		Skipped = false;
	}
}