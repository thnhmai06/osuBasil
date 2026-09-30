using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps;

/// <summary>Stores beatmap difficulties.</summary>
public interface IBeatmapRepository
{
	/// <summary>Stores a beatmap, replacing any stored beatmap with the same id.</summary>
	/// <param name="beatmap">The beatmap to store.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	Task SaveAsync(Beatmap beatmap, CancellationToken cancellationToken = default);

	/// <summary>Deletes the stored beatmaps of a beatmapset that are not in a given list.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="keep">The beatmaps the set still has.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	Task RetainAsync(Beatmapset set, IReadOnlyCollection<Beatmap> keep, CancellationToken cancellationToken = default);
}