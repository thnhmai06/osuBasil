using Basil.Domain.Game.Mechanics;
using Basil.Shared.Utilities;

namespace Basil.Domain.Multiplayer;

public sealed class MatchSettings : IMatchSettings
{
	public GameMode Mode
	{
		get;
		set
		{
			value.ThrowIfInvalid();
			if (!Mods.IsValid(value))
				throw new ArgumentException("The current mods are not valid in that mode.", nameof(value));
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
				throw new ArgumentException("With freemods on, the room mods may only be speed-changing mods.",
					nameof(value));
			field = value;
		}
	} = GameMods.NoMod;

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