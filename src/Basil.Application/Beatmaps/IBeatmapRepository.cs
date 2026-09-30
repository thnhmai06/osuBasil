using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps;

/// <summary>Stores beatmap difficulties.</summary>
public interface IBeatmapRepository
{
	/// <summary>Stores a beatmap, replacing any stored beatmap with the same id.</summary>
	/// <param name="beatmap">The beatmap to store.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	Task SaveAsync(Beatmap beatmap, CancellationToken cancellationToken = default);
}