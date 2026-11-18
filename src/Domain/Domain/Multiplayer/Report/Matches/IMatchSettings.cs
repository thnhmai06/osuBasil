using Basil.Domain.Game.Mechanics;

namespace Basil.Domain.Multiplayer;

public interface IMatchSettings
{
	GameMode Mode { get; }

	GameMods Mods { get; }

	bool Freemods { get; }

	GameTeamType TeamType { get; }

	GameWinCondition WinCondition { get; }

	int Seed { get; }
}