using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;

namespace Basil.Application.Abstractions.Beatmaps;

/// <summary>
///     Every calculation genuinely backed by the ppy.osu.Game/osu.Framework packages, consolidated
///     into one port rather than one interface per calculation (see <c>HitCounts.CalculateAccuracy</c>
///     in Basil.Domain for the one deliberate exception — a standard, documented osu! formula with no
///     equivalent static library function, kept hand-rolled since <c>Basil.Domain</c> has zero package
///     dependencies by design).
/// </summary>
public interface IOsuCalculator
{
    /// <summary>
    ///     Star rating and per-mode hit-object counts for the beatmap at
    ///     <paramref name="beatmapFilePath" /> under the given ruleset (<paramref name="mode" />) and
    ///     mods.
    /// </summary>
    BeatmapAnalysis Analyze(string beatmapFilePath, GameMode mode, Mods mods);

    /// <summary>
    ///     Wraps osu.Framework's own <c>ComputeMD5Hash(Stream)</c> — the same hashing osu.Game itself
    ///     uses internally for beatmap content identity — so beatmap md5s are computed the same way
    ///     the real client/library computes them, not a separately hand-rolled hash.
    /// </summary>
    string ComputeBeatmapMd5(byte[] beatmapBytes);
}

/// <summary>Star rating plus per-mode hit-object counts (e.g. <c>{"circle":120,"slider":45,"spinner":2}</c> for osu!std).</summary>
public sealed record BeatmapAnalysis(double StarRating, IReadOnlyDictionary<string, int> ObjectCounts);
