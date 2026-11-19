using Basil.Domain.Mechanics;

namespace Basil.Domain.Multiplayer.Room;

public sealed record RoomSettingsChange(
	string? Name = null,
	BeatmapReference? Beatmap = null,
	GameMode? Mode = null,
	GameMods? Mods = null,
	bool? Freemods = null,
	GameTeamType? TeamType = null,
	GameWinCondition? WinCondition = null,
	string? Password = null,
	int? Size = null,
	bool ClearBeatmap = false,
	bool? IsPrivate = null)
{
	public bool AltersTheRound => Beatmap is not null || ClearBeatmap || Mode is not null || Mods is not null ||
	                              Freemods is not null || TeamType is not null || WinCondition is not null;
}