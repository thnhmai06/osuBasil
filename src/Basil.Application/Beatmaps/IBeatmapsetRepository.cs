using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps;

/// <summary>Stores beatmapsets.</summary>
public interface IBeatmapsetRepository
{
	/// <summary>Gets a beatmapset by id.</summary>
	/// <param name="id">The beatmapset id.</param>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	/// <returns>The beatmapset, or <see langword="null" /> when none has that id.</returns>
	ValueTask<Beatmapset?> GetAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>Stores a beatmapset, replacing any stored beatmapset with the same id.</summary>
	/// <param name="set">The beatmapset to store.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	Task SaveAsync(Beatmapset set, CancellationToken cancellationToken = default);
}