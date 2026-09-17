using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer.Records;
using Basil.Domain.Scores;

namespace Basil.Application.Multiplayer;

/// <summary>
///     The settings a new match is created with: its room name, password, initial beatmap, and
///     ruleset options. Carries no slot state — a match always starts with every slot empty.
/// </summary>
/// <param name="Name">The room's name.</param>
/// <param name="Password">The room's password, or empty for none.</param>
/// <param name="MapName">The name of the initially selected beatmap.</param>
/// <param name="MapId">The id of the initially selected beatmap, or <see langword="null" /> when none is chosen.</param>
/// <param name="MapMd5">The md5 of the initially selected beatmap, or <see langword="null" /> when none is chosen.</param>
/// <param name="HostId">The id of the player the creation request claims as host.</param>
/// <param name="Mode">The game mode the room plays in.</param>
/// <param name="Mods">The mods applied to the whole room.</param>
/// <param name="WinCondition">The condition that decides the winner of a round.</param>
/// <param name="TeamType">The team arrangement used for the room.</param>
/// <param name="FreeMods">Whether freemod mode is enabled.</param>
/// <param name="Seed">The room's random seed.</param>
public sealed record MatchCreationData(
	string Name,
	string Password,
	string MapName,
	int? MapId,
	string? MapMd5,
	int HostId,
	GameMode Mode,
	Mods Mods,
	MatchWinCondition WinCondition,
	MatchTeamType TeamType,
	bool FreeMods,
	int Seed);