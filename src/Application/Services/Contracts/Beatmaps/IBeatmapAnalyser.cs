using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;

namespace Basil.Application.Services.Contracts.Beatmaps;

/// <summary>
///     Computes the beatmap gameplay stats that the server itself cannot derive from the raw
///     beatmap file: mod- and mode-affected difficulty and per-mode hit-object counts.
/// </summary>
/// <remarks>
///     Nothing in scoring, leaderboards, or match win conditions depends on these calculations: they
///     feed star rating and difficulty display only.
/// </remarks>
public interface IBeatmapAnalyser
{
	/// <summary>Analyzes the beatmap file with the given content and returns its gameplay stats.</summary>
	/// <param name="content">The .osu file, read from its current position; not disposed.</param>
	/// <param name="mode">The ruleset to analyze the beatmap under.</param>
	/// <param name="mods">The mods whose difficulty adjustments the analysis should apply.</param>
	/// <returns>The analyzed difficulty stats and per-mode hit-object counts.</returns>
	BeatmapAnalysis Analyze(Stream content, GameMode mode, GameMods mods);

	/// <summary>Analyzes a stored beatmap under a mod combination.</summary>
	/// <param name="beatmap">The beatmap, whose stored .osu file is read the first time it is analyzed.</param>
	/// <param name="mods">The mods whose difficulty adjustments the analysis should apply.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The analysis, or <see langword="null" /> when the beatmap's .osu file is not stored.</returns>
	/// <remarks>The file is read once for each beatmap instance; analyzing it again under other mods reads no file.</remarks>
	Task<BeatmapAnalysis?> AnalyzeAsync(BeatmapRecord beatmap, GameMods mods, CancellationToken cancellationToken = default);
}

/// <summary>The result of analyzing a beatmap under a specific mode and mod combination.</summary>
/// <param name="Difficulty">The mod- and mode-affected gameplay stats.</param>
/// <param name="Objects">The per-mode hit-object counts of the beatmap.</param>
public sealed record BeatmapAnalysis(BeatmapDifficulty Difficulty, BeatmapObjects Objects);
