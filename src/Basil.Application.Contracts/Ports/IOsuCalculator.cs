using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;

namespace Basil.Application.Contracts.Ports;

/// <summary>
///     Computes the beatmap gameplay stats that the server itself cannot derive from the raw
///     beatmap file: mod- and mode-affected difficulty, per-mode hit-object counts, and the content
///     MD5.
/// </summary>
/// <remarks>
///     Nothing in scoring, leaderboards, or match win conditions depends on these calculations: they
///     feed star rating and difficulty display only.
/// </remarks>
public interface IOsuCalculator
{
	/// <summary>Analyzes the beatmap file at the given path and returns its gameplay stats.</summary>
	/// <param name="beatmapFilePath">The path to the .osu file on disk.</param>
	/// <param name="mode">The ruleset to analyze the beatmap under.</param>
	/// <param name="mods">The mods whose difficulty adjustments the analysis should apply.</param>
	/// <returns>The analyzed difficulty stats and per-mode hit-object counts.</returns>
	BeatmapAnalysis Analyze(string beatmapFilePath, GameMode mode, GameMods mods);

	/// <summary>Computes the content MD5 of the given beatmap file bytes.</summary>
	/// <param name="beatmapBytes">The raw bytes of the .osu file.</param>
	/// <returns>The lowercase-hex MD5 of the file contents.</returns>
	string ComputeBeatmapMd5(byte[] beatmapBytes);
}

/// <summary>The result of analyzing a beatmap under a specific mode and mod combination.</summary>
/// <param name="Difficulty">The mod- and mode-affected gameplay stats.</param>
/// <param name="Objects">The per-mode hit-object counts of the beatmap.</param>
public sealed record BeatmapAnalysis(Difficulty Difficulty, BeatmapObjects Objects);