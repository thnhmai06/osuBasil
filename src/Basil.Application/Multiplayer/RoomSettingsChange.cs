using Basil.Domain.Mechanics;

namespace Basil.Application.Multiplayer;

/// <summary>A change to a room's settings; a <see langword="null" /> field is left unchanged.</summary>
/// <param name="Name">The room's new name.</param>
/// <param name="Beatmap">The beatmap the room selects.</param>
/// <param name="Mode">The game mode the room plays.</param>
/// <param name="Mods">The mods applied to the whole room.</param>
/// <param name="Freemods">Whether players choose their own mods.</param>
/// <param name="TeamType">The room's team arrangement.</param>
/// <param name="WinCondition">The condition that decides the winner of a round.</param>
/// <param name="Password">The room's new password.</param>
/// <param name="Size">The number of usable slots, from 1 to 16.</param>
public sealed record RoomSettingsChange(
	string? Name = null,
	BeatmapReference? Beatmap = null,
	GameMode? Mode = null,
	GameMods? Mods = null,
	bool? Freemods = null,
	GameTeamType? TeamType = null,
	GameWinCondition? WinCondition = null,
	string? Password = null,
	int? Size = null);
