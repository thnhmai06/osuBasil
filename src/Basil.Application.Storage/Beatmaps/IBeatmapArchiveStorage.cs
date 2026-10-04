using Basil.Domain.Beatmaps;

namespace Basil.Application.Storage.Beatmaps;

/// <summary>Stores the <c>.osz</c> archives of beatmapsets.</summary>
public interface IBeatmapArchiveStorage
{
	/// <summary>Stores the archive of a beatmapset, replacing any archive already stored for it.</summary>
	/// <param name="set">The beatmapset the archive belongs to.</param>
	/// <param name="content">The archive bytes.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task SaveAsync(Beatmapset set, Stream content, CancellationToken cancellationToken = default);

	/// <summary>Opens the stored archive of a beatmapset.</summary>
	/// <param name="set">The beatmapset the archive belongs to.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The archive bytes, or <see langword="null" /> when no archive is stored for the set.</returns>
	Task<Stream?> OpenAsync(Beatmapset set, CancellationToken cancellationToken = default);

	/// <summary>Deletes the stored archive of a beatmapset; deleting a missing archive does nothing.</summary>
	/// <param name="set">The beatmapset the archive belongs to.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default);
}