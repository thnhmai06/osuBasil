using Basil.Domain.Mechanics;

namespace Basil.Domain.Multiplayer.Report.Matches;

public interface IMatchSettings
{
	GameMode Mode { get; }

	GameMods Mods { get; }

	bool Freemods { get; }

	GameTeamType TeamType { get; }

	GameWinCondition WinCondition { get; }

	int Seed { get; }
}