using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;

namespace Basil.Application.Beatmaps.Ports;

/// <summary>
///     Calculates the star rating and pp of a stored beatmap under a mode and a mod combination.
/// </summary>
/// <remarks>
///     The calculation of a combination is done once and reused, and calls for the same combination at the same time
///     share it. The numbers are for display: none of them decides a ranking, a leaderboard or a match win condition.
///     A beatmap whose file the server does not have cannot be calculated.
/// </remarks>
public interface IBeatmapCalculator
{
	/// <summary>Gets the star rating of a beatmap.</summary>
	/// <param name="beatmap">The beatmap.</param>
	/// <param name="mode">The game mode it is played in.</param>
	/// <param name="mods">The mods of the play; only the ones that change the difficulty count.</param>
	/// <param name="ct">A token that cancels the wait for the result.</param>
	/// <returns>The star rating.</returns>
	/// <exception cref="InvalidOperationException">The beatmap cannot be calculated: its file is missing or unreadable.</exception>
	Task<double> GetStarsAsync(BeatmapRecord beatmap, GameMode mode, GameMods mods, CancellationToken ct = default);

	/// <summary>Gets the pp of a play of a beatmap, and the pp of a perfect play of it.</summary>
	/// <param name="beatmap">The beatmap.</param>
	/// <param name="mode">The game mode it was played in.</param>
	/// <param name="mods">The mods of the play.</param>
	/// <param name="hits">The hit judgments of the play.</param>
	/// <param name="combo">The highest combo of the play.</param>
	/// <param name="ct">A token that cancels the wait for the result.</param>
	/// <returns>The pp of the play and of a perfect play under the same mods.</returns>
	/// <exception cref="InvalidOperationException">The beatmap cannot be calculated: its file is missing or unreadable.</exception>
	Task<BeatmapPerformance> GetPerformanceAsync(BeatmapRecord beatmap, GameMode mode, GameMods mods, HitCounts hits,
		int combo, CancellationToken ct = default);
}

/// <summary>The pp of a play and of a perfect play of the same beatmap under the same mods.</summary>
/// <param name="Pp">The pp of the play: finite and at least zero.</param>
/// <param name="MaxPp">The pp of a perfect play: finite and at least <paramref name="Pp" />.</param>
public readonly record struct BeatmapPerformance(double Pp, double MaxPp);
