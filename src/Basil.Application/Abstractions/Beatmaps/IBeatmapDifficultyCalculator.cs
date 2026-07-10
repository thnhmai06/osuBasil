using Basil.Domain;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Abstractions.Beatmaps;

/// <summary>
///     Computes a beatmap's star rating under a given mode + mod combination. Basil
///     has no player-facing pp/performance system — leaderboards rank by raw score — so this
///     port only exposes difficulty (star rating), which depends solely on the beatmap, mode,
///     and mods, not on any player's hit results.
/// </summary>
public interface IBeatmapDifficultyCalculator
{
    /// <summary>
    ///     Calculates the star rating for the beatmap at <paramref name="beatmapFilePath" /> under
    ///     the given ruleset (<paramref name="mode" />) and mods.
    /// </summary>
    double CalculateStarRating(string beatmapFilePath, GameMode mode, Mods mods);
}