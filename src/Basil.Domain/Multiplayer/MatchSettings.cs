using Basil.Domain.Mechanics;
using Basil.Domain.Utilities;

namespace Basil.Domain.Multiplayer;

/// <summary>
///     Represents the settings of an osu! multiplayer room: the selected beatmap, game mode,
///     mods, team arrangement, and win condition.
/// </summary>
public sealed class MatchSettings
{
	/// <summary>Gets the game mode the room plays in.</summary>
	public GameMode Mode
	{
		get;
		private set
		{
			value.ThrowIfUndefined();
			field = value;
		}
	} = GameMode.Standard;

	/// <summary>Switches the game mode, dropping selected mods the new mode does not allow.</summary>
	/// <param name="mode">The game mode to switch to.</param>
	public void SwitchMode(GameMode mode)
	{
		Mode = mode;
		Mods = Mods.RemoveInvalidMods(mode);
	}

	/// <summary>Gets or sets the mods applied to the whole room.</summary>
	public GameMods Mods
	{
		get;
		set
		{
			value.ThrowIfInvalid(Mode);
			field = value;
		}
	} = GameMods.NoMod;

	/// <summary>Gets or sets a value that indicates whether freemod mode is enabled.</summary>
	public bool Freemods { get; set; } = false;

	/// <summary>Gets or sets the team arrangement used for the room.</summary>
	public GameTeamType TeamType
	{
		get;
		set
		{
			value.ThrowIfUndefined();
			field = value;
		}
	} = GameTeamType.HeadToHead;

	/// <summary>Gets or sets the condition that decides the winner of a round.</summary>
	public GameWinCondition WinCondition
	{
		get;
		set
		{
			value.ThrowIfUndefined();
			field = value;
		}
	} = GameWinCondition.Score;

	/// <summary>Gets the room's random seed, broadcast to clients as part of the match's data.</summary>
	public int Seed { get; init; } = 0;

	public MatchSettings Clone()
	{
		return (MatchSettings)MemberwiseClone();
	}
}