using Basil.Domain.Beatmaps;
using Basil.Domain.Game;

namespace Basil.Application.Rulesets.Ports;

internal interface IBeatmapAnalyser
{
	/// <summary>Calculates the difficulty and object counts of a beatmap file; null when the calculation stack is not available.</summary>
	BeatmapAnalysis? Analyze(Stream content, GameMode mode, GameMods mods);
}

public sealed record BeatmapAnalysis(BeatmapDifficulty Difficulty, BeatmapObjectCount ObjectCount);