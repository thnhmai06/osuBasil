using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps;

/// <summary>Stores the <c>.osz</c> archives of beatmapsets.</summary>
public interface IBeatmapArchiveStorage
{
	/// <summary>Stores the archive of a beatmapset, replacing any archive already stored for it.</summary>
	/// <param name="set">The beatmapset the archive belongs to.</param>
	/// <param name="content">The archive bytes.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	Task SaveAsync(Beatmapset set, Stream content, CancellationToken cancellationToken = default);
}