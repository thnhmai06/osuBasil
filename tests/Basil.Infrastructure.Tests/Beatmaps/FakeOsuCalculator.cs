using System.Security.Cryptography;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;

namespace Basil.Infrastructure.Tests.Beatmaps;

/// <summary>Constant stand-in for the real ppy-backed calculator — these tests aren't about star rating/object-count math.</summary>
internal sealed class FakeOsuCalculator : IOsuCalculator
{
    public BeatmapAnalysis Analyze(string beatmapFilePath, GameMode mode, Mods mods)
    {
        return new BeatmapAnalysis(1.23, new Dictionary<string, int> { ["circle"] = 1 });
    }

    public string ComputeBeatmapMd5(byte[] beatmapBytes)
    {
        return Convert.ToHexStringLower(MD5.HashData(beatmapBytes));
    }
}
