using Basil.Domain.Beatmaps;
using Basil.Domain.Game;

namespace Basil.Application.Rulesets.Ports;

internal interface IBeatmapAnalyser
{
	BeatmapAnalysis Analyze(Stream content, GameMode mode, GameMods mods);
}

public sealed record BeatmapAnalysis(BeatmapDifficulty Difficulty, BeatmapObjectCount ObjectCount);