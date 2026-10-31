using Basil.Domain.Common;
using Basil.Domain.Mechanics;

namespace Basil.Domain.Multiplayer;

/// <summary>
///     Represents the settings of an osu! multiplayer room: the selected beatmap, game mode,
///     mods, team arrangement, and win condition.
/// </summary>
public sealed class MatchSettings
{
	/// <summary>Gets or sets the game mode the room plays in.</summary>
	/// <exception cref="ArgumentException">The current <see cref="Mods" /> are not valid in the new mode.</exception>
	public GameMode Mode
	{
		get;
		set
		{
			value.ThrowIfUndefined();
			if (!Mods.IsValid(value))
				throw new ArgumentException("The current mods are not valid in that mode.", nameof(value));
			field = value;
		}
	} = GameMode.Standard;

	/// <summary>Gets or sets the mods applied to the whole room.</summary>
	/// <exception cref="ArgumentException">
	///     The mods are not valid in the current <see cref="Mode" />, or <see cref="Freemods" /> is on and the mods
	///     include any that are not speed-changing.
	/// </exception>
	public GameMods Mods
	{
		get;
		set
		{
			value.ThrowIfInvalid(Mode);
			if (Freemods && (value & ~GameModsGroups.SpeedChanging) != GameMods.NoMod)
				throw new ArgumentException("With freemods on, the room mods may only be speed-changing mods.",
					nameof(value));
			field = value;
		}
	} = GameMods.NoMod;

	/// <summary>Gets or sets a value that indicates whether freemod mode is enabled.</summary>
	/// <exception cref="ArgumentException">
	///     Freemods is being turned on while the current <see cref="Mods" /> include any that are not speed-changing.
	/// </exception>
	public bool Freemods
	{
		get;
		set
		{
			if (value && (Mods & ~GameModsGroups.SpeedChanging) != GameMods.NoMod)
				throw new ArgumentException("Freemods needs the room mods to be speed-changing mods only.",
					nameof(value));
			field = value;
		}
	} = false;

	/// <summary>Gets or sets the team arrangement used for the room.</summary>
	public GameTeamType TeamType
	{
		get;
		set => field = value.ThrowIfUndefined();
	} = GameTeamType.HeadToHead;

	/// <summary>Gets or sets the condition that decides the winner of a round.</summary>
	public GameWinCondition WinCondition
	{
		get;
		set => field = value.ThrowIfUndefined();
	} = GameWinCondition.Score;

	/// <summary>Gets the room's random seed, broadcast to clients as part of the match's data.</summary>
	public int Seed { get; init; } = 0;

	/// <summary>Returns a copy of these settings that can be changed without affecting this instance.</summary>
	/// <returns>A new set of settings with the same values.</returns>
	public MatchSettings Clone()
	{
		return (MatchSettings)MemberwiseClone();
	}
}