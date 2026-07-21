using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;

namespace Basil.Infrastructure.Tests.Beatmaps;

/// <summary>Constant stand-in for the real ppy-backed calculator — these tests aren't about star rating math.</summary>
internal sealed class FakeDifficultyCalculator : IDifficultyCalculator
{
    public double CalculateStarRating(string beatmapFilePath, GameMode mode, Mods mods)
    {
        return 1.23;
    }
}
