using Basil.Domain.Game;

namespace Basil.Domain.Multiplayer.Report.Matches;

/// <summary>The settings a match is played with.</summary>
public interface IMatchSettings
{
	/// <summary>Gets the game mode.</summary>
	GameMode Mode { get; }

	/// <summary>Gets the mods that apply to every player; under freemods only the speed-changing ones.</summary>
	GameMods Mods { get; }

	/// <summary>Gets whether players choose their own mods.</summary>
	bool Freemods { get; }

	/// <summary>Gets whether players compete head to head or in teams.</summary>
	GameTeamType TeamType { get; }

	/// <summary>Gets the measure that decides who wins a round.</summary>
	GameWinCondition WinCondition { get; }

	/// <summary>Gets the random seed of the match, shared by every player so that a mod such as Random plays out the same for all.</summary>
	int Seed { get; }
}