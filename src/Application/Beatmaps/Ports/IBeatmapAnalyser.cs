using Basil.Domain.Beatmaps;
using Basil.Domain.Game.Mechanics;

namespace Basil.Application.Beatmaps.Ports;

public interface IBeatmapAnalyser
{
	BeatmapAnalysis Analyze(Stream content, GameMode mode, GameMods mods);
}

public sealed record BeatmapAnalysis(BeatmapDifficulty Difficulty, BeatmapObjectCount ObjectCount);