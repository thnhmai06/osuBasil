namespace Bancho.Application.Abstractions.Beatmaps;

/// <summary>
/// Computes a beatmap's star rating under a given mod combination. bancho-net has no
/// player-facing pp/performance system — leaderboards rank by raw score, matching bancho.py's
/// vanilla-mode "score" scoring metric — so this port only exposes difficulty (star rating),
/// which depends solely on the beatmap and mods, not on any player's hit results.
/// </summary>
public interface IBeatmapDifficultyCalculator
{
    /// <summary>
    /// Calculates the star rating for the beatmap at <paramref name="beatmapFilePath"/> under
    /// the given legacy osu! mod bitflags.
    /// </summary>
    double CalculateStarRating(string beatmapFilePath, int mods);
}
