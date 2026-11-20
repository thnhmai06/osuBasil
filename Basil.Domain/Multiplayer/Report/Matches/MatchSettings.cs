using Basil.Domain.Game;
using Basil.Shared.Utilities;

namespace Basil.Domain.Multiplayer.Report.Matches;

public sealed class MatchSettings : IMatchSettings
{
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

	public GameTeamType TeamType
	{
		get;
		set => field = value.ThrowIfInvalid();
	} = GameTeamType.HeadToHead;

	public GameWinCondition WinCondition
	{
		get;
		set => field = value.ThrowIfInvalid();
	} = GameWinCondition.Score;

	public int Seed { get; init; } = 0;

	public MatchSettings Clone()
	{
		return (MatchSettings)MemberwiseClone();
	}
}