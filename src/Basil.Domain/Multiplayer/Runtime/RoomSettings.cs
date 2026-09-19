using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer.Records;

namespace Basil.Domain.Multiplayer.Runtime;

public class RoomSettings
{
	/// <summary>Gets or sets the currently selected beatmap.</summary>
	public Beatmap? Beatmap { get; set; } // null = unset (not "Beatmap not found")

	/// <summary>Gets or sets the game mode the room plays in.</summary>
	public GameMode Mode { get; set; } = GameMode.Standard;

	/// <summary>Gets or sets the mods applied to the whole room.</summary>
	public GameMods Mods { get; set; } = GameMods.NoMod;

	/// <summary>Gets or sets a value that indicates whether freemod mode is enabled.</summary>
	public bool Freemods { get; set; } = false;

	/// <summary>Gets or sets the team arrangement used for the room.</summary>
	public GameTeamType TeamType { get; set; } = GameTeamType.HeadToHead;

	/// <summary>Gets or sets the condition that decides the winner of a round.</summary>
	public GameWinCondition WinCondition { get; set; } = GameWinCondition.Score;

	/// <summary>Gets the room's random seed, broadcast to clients as part of the match's data.</summary>
	public int Seed { get; init; }

	public MatchSettings ToMatchSettings()
	{
		if (Beatmap is null) throw new ArgumentNullException(nameof(Beatmap), "The Beatmap must be set.");
		return (MatchSettings)MemberwiseClone();
	}
}