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
		var difficulty = new Difficulty(mode, 180, TimeSpan.FromSeconds(90), 4, 9, 8, 5, 1.23);
		return new BeatmapAnalysis(difficulty, new Dictionary<string, int> { ["circle"] = 1 }, 150);
	}

	public string ComputeBeatmapMd5(byte[] beatmapBytes)
	{
		return Convert.ToHexStringLower(MD5.HashData(beatmapBytes));
	}
}