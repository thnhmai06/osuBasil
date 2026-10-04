namespace Basil.Application.Contracts.Beatmaps;

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
	/// <param name="content">The bytes of the .osu file.</param>
	/// <param name="mode">The ruleset to analyze the beatmap under.</param>
	/// <param name="mods">The mods whose difficulty adjustments the analysis should apply.</param>
	/// <returns>The analyzed difficulty stats and per-mode hit-object counts.</returns>
	BeatmapAnalysis Analyze(byte[] content, GameMode mode, GameMods mods);
}

/// <summary>The result of analyzing a beatmap under a specific mode and mod combination.</summary>
/// <param name="Difficulty">The mod- and mode-affected gameplay stats.</param>
/// <param name="Objects">The per-mode hit-object counts of the beatmap.</param>
public sealed record BeatmapAnalysis(Difficulty Difficulty, BeatmapObjects Objects);