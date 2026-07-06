using Bancho.Application.Abstractions;

namespace Bancho.Infrastructure.Performance;

/// <inheritdoc cref="IBeatmapDifficultyCalculator" />
public sealed class NativeBeatmapDifficultyCalculator : IBeatmapDifficultyCalculator
{
    public double CalculateStarRating(string beatmapFilePath, int mods)
    {
        var resultCode = BanchoPpNative.bancho_pp_calculate_difficulty(
            beatmapFilePath,
            unchecked((uint)mods),
            out var stars);

        if (resultCode != 0)
        {
            throw new InvalidOperationException(
                $"bancho_pp_calculate_difficulty failed with code {resultCode} for beatmap '{beatmapFilePath}'.");
        }

        return stars;
    }
}
