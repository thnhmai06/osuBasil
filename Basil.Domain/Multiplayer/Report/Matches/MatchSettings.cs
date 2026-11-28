using Basil.Domain.Game;
using Basil.Shared.Utilities;

namespace Basil.Domain.Multiplayer.Report.Matches;

/// <summary>The editable settings a match is played with.</summary>
public sealed class MatchSettings : IMatchSettings
{
	/// <summary>Gets or sets the game mode.</summary>
	/// <exception cref="InvalidModsException">The current mods are not valid for the new mode.</exception>
	public GameMode Mode
	{
		get;
		set
		{
			value.ThrowIfInvalid();
			if (!Mods.IsValid(value))
				throw new InvalidModsException();
			field = value;
		}
	} = GameMode.Standard;

	/// <summary>Gets or sets the mods that apply to every player.</summary>
	/// <exception cref="InvalidModsException">The mods are not valid for the mode, or include a non-speed-changing mod while freemods is on.</exception>
	public GameMods Mods
	{
		get;
		set
		{
			value.ThrowIfInvalid(Mode);
			if (Freemods && (value & ~GameModsGroups.SpeedChanging) != GameMods.NoMod)
				throw new InvalidModsException();
			field = value;
		}
	} = GameMods.NoMod;

	/// <summary>Gets or sets whether players choose their own mods.</summary>
	/// <exception cref="InvalidModsException">Freemods is turned on while the room mods include a non-speed-changing mod.</exception>
	public bool Freemods
	{
		get;
		set
		{
			if (value && (Mods & ~GameModsGroups.SpeedChanging) != GameMods.NoMod)
				throw new InvalidModsException();
			field = value;
		}
	} = false;

	/// <summary>Gets or sets whether players compete head to head or in teams.</summary>
	public GameTeamType TeamType
	{
		get;
		set => field = value.ThrowIfInvalid();
	} = GameTeamType.HeadToHead;

	/// <summary>Gets or sets the measure that decides who wins a round.</summary>
	public GameWinCondition WinCondition
	{
		get;
		set => field = value.ThrowIfInvalid();
	} = GameWinCondition.Score;

	/// <summary>Gets the random seed of the match, shared by every player so that a mod such as Random plays out the same for all.</summary>
	public int Seed { get; init; } = 0;

	/// <summary>Gets an independent copy of the settings.</summary>
	public MatchSettings Clone()
	{
		return (MatchSettings)MemberwiseClone();
	}
}