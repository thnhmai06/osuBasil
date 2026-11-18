using Basil.Application.Beatmaps.Ports;
using Basil.Domain.Beatmaps;
using Basil.Domain.Game.Mechanics;

namespace Basil.Infrastructure.Beatmaps.Logic;

internal sealed class DifficultyAnalyser(IBeatmapAnalyser analyser)
{
	public async Task<BeatmapAnalysis> AnalyseAsync(BeatmapsetDifficulty difficulty, BeatmapRecord? stored,
		CancellationToken ct)
	{
		if (stored is { Metadata.Difficulty.Star: > 0 })
			return new BeatmapAnalysis(stored.Metadata.Difficulty, stored.Metadata.ObjectCount);

		try
		{
			var stream = await difficulty.File.OpenAsync(ct);
			await using (stream)
			{
				return analyser.Analyze(stream, difficulty.Mode, GameMods.NoMod);
			}
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			return EmptyAnalysis(difficulty.Mode);
		}
	}

	private static BeatmapAnalysis EmptyAnalysis(GameMode mode)
	{
		var objects = BeatmapObjectCount.Empty(mode);
		return new BeatmapAnalysis(new BeatmapDifficulty(mode, 0, TimeSpan.Zero, 0, 0, 0, 0, 0), objects);
	}
}