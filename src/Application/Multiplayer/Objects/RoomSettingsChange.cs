using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Domain.Game.Mechanics;

namespace Basil.Application.Multiplayer.Objects;

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