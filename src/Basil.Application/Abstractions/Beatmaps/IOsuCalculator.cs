using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;

namespace Basil.Application.Abstractions.Beatmaps;

/// <summary>
///     Computes the beatmap gameplay stats that the server itself cannot derive from the raw
///     beatmap file: mod- and mode-affected difficulty, per-mode hit-object counts, and the
///     content md5.
/// </summary>
/// <remarks>
///     Every such calculation is consolidated into this one port rather than one interface per
///     calculation. The single deliberate exception is <c>HitCounts.CalculateAccuracy</c> in
///     Basil.Domain, a standard, documented osu! formula with no equivalent static library
///     function, which is kept hand-rolled because Basil.Domain has no package dependencies by
///     design. Nothing in scoring, leaderboards, or match win conditions depends on these
///     calculations: they feed star rating and difficulty display only.
/// </remarks>
public interface IOsuCalculator
{
	/// <summary>
	///     Analyzes the beatmap file at the given path and returns its gameplay stats.
	/// </summary>
	/// <param name="beatmapFilePath">The path to the .osu file on disk.</param>
	/// <param name="mode">The ruleset to analyze the beatmap under.</param>
	/// <param name="mods">The mods whose difficulty adjustments the analysis should apply.</param>
	/// <returns>The analyzed difficulty stats and per-mode hit-object counts.</returns>
	/// <remarks>
	///     The returned <see cref="Difficulty" /> carries every mod-affected stat (star rating, BPM,
	///     length, CS/AR/OD/HP) computed for the exact mode and mods passed in, plus the per-mode
	///     hit-object counts and max combo. These fields are computed from the playable beatmap, not
	///     read off the raw .osu decode, whose length, max combo, and BPM values are always zero.
	/// </remarks>
	BeatmapAnalysis Analyze(string beatmapFilePath, GameMode mode, Mods mods);

	/// <summary>
	///     Computes the content md5 of the given beatmap file bytes.
	/// </summary>
	/// <param name="beatmapBytes">The raw bytes of the .osu file.</param>
	/// <returns>The lowercase-hex md5 of the file contents.</returns>
	/// <remarks>
	///     Computes the same md5 that the osu! client itself derives for a beatmap's content, which
	///     guarantees the stored md5 matches the identity the client and its library use, rather
	///     than a separately hand-rolled hash.
	/// </remarks>
	string ComputeBeatmapMd5(byte[] beatmapBytes);
}

/// <summary>
///     The result of analyzing a beatmap under a specific mode and mod combination.
/// </summary>
/// <param name="Difficulty">The mod- and mode-affected gameplay stats.</param>
/// <param name="BeatmapObjectCounts">The per-mode hit-object counts of the beatmap.</param>
/// <remarks>
///     <see cref="Difficulty" /> already carries every mod- and mode-affected stat (star rating,
///     BPM, length, CS/AR/OD/HP) computed for the exact mode and mods passed to
///     <see cref="IOsuCalculator.Analyze" />; see the implementation's analysis method for how each
///     field is derived, and why the raw decoder's own length, max combo, BPM, and difficulty
///     settings are never read directly. <see cref="BeatmapObjectCounts" /> is its own concrete
///     subtype per mode and also carries the max combo; it is not part of
///     <see cref="Difficulty" /> because it does not vary with mods.
/// </remarks>
public sealed record BeatmapAnalysis(
	Difficulty Difficulty,
	BeatmapObjectCounts BeatmapObjectCounts);