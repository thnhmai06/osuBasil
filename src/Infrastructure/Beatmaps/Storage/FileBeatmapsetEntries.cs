using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Content;

namespace Basil.Infrastructure.Beatmaps.Storage;

internal sealed class FileBeatmapsetArchive(string path, string name, ContentMetadata metadata, string? packedFrom = null)
	: BeatmapsetArchive(name, metadata)
{
	internal string Path => path;

	internal string? PackedFrom => packedFrom;

	public override Task<Stream> OpenAsync(CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read,
			FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous));
	}
}

internal sealed class FileBeatmapsetFolder(string path, string name, int? id, bool exists, BeatmapsetArchive? archive)
	: BeatmapsetFolder(name, id, exists, archive)
{
	internal string Path => path;
}