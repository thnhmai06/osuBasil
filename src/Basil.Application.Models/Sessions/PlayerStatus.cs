using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Models.Sessions;

/// <summary>
///     The presence state a game client currently reports: what it is doing, and (while playing or
///     selecting) which beatmap, mods, and mode.
/// </summary>
/// <param name="Activity">The activity the client is currently reporting.</param>
/// <param name="Text">The free-form status text shown to other players alongside the activity.</param>
/// <param name="BeatmapMd5">The MD5 of the beatmap being played or selected, or <see langword="null" /> for none.</param>
/// <param name="BeatmapId">The id of the beatmap being played or selected, or <see langword="null" /> for none.</param>
/// <param name="GameMods">The mods currently active.</param>
/// <param name="GameMode">The game mode currently selected.</param>
public sealed record PlayerStatus(
	UserActivity Activity,
	string Text,
	string? BeatmapMd5,
	int? BeatmapId,
	GameMods GameMods,
	GameMode GameMode)
{
	/// <summary>The status of a client that has just logged in and reported nothing yet.</summary>
	public static readonly PlayerStatus Idle =
		new(UserActivity.Idle, string.Empty, null, null, GameMods.NoMod, GameMode.Standard);
}