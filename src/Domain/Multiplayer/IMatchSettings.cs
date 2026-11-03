using Basil.Domain.Mechanics;

namespace Basil.Domain.Multiplayer;

/// <summary>The settings of an osu! multiplayer room as a reader sees them: the values, without a way to change them.</summary>
public interface IMatchSettings
{
	/// <summary>Gets the game mode the room plays in.</summary>
	GameMode Mode { get; }

	/// <summary>Gets the mods applied to the whole room.</summary>
	GameMods Mods { get; }

	/// <summary>Gets a value that indicates whether freemod mode is enabled.</summary>
	bool Freemods { get; }

	/// <summary>Gets the team arrangement used for the room.</summary>
	GameTeamType TeamType { get; }

	/// <summary>Gets the condition that decides the winner of a round.</summary>
	GameWinCondition WinCondition { get; }

	/// <summary>Gets the room's random seed, broadcast to clients as part of the match's data.</summary>
	int Seed { get; }
}
