using Basil.Application.Persistence.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;

namespace Basil.Application.Capabilities.Beatmaps;

/// <summary>Analyses the difficulties of a beatmapset.</summary>
internal sealed class DifficultyAnalyser(IBeatmapAnalyser analyser)
{
	/// <summary>
	///     Analyses a difficulty, reusing the analysis of the stored beatmap with the same file when it has a star
	///     rating.
	/// </summary>
	/// <param name="difficulty">The difficulty to analyse.</param>
	/// <param name="stored">The stored beatmap of the same file, or <see langword="null" /> when there is none.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>A difficulty that cannot be analysed gets a zero star rating and no objects.</remarks>
	public async Task<BeatmapAnalysis> AnalyseAsync(BeatmapsetDifficulty difficulty, BeatmapRecord? stored,
		CancellationToken cancellationToken)
	{
		if (stored is { Metadata.Difficulty.Star: > 0 })
			return new BeatmapAnalysis(stored.Metadata.Difficulty, stored.Metadata.Objects);

		try
		{
			var stream = await difficulty.File.OpenAsync(cancellationToken).ConfigureAwait(false);
			await using (stream.ConfigureAwait(false))
			{
				return analyser.Analyze(stream, difficulty.Mode, GameMods.NoMod);
			}
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			return EmptyAnalysis(difficulty.Mode);
		}
	}

	/// <summary>A zero difficulty and no objects in a game mode.</summary>
	/// <param name="mode">The game mode.</param>
	private static BeatmapAnalysis EmptyAnalysis(GameMode mode)
	{
		var objects = BeatmapObjects.Empty(mode);
		return new BeatmapAnalysis(new BeatmapDifficulty(mode, 0, TimeSpan.Zero, 0, 0, 0, 0, 0), objects);
	}
}