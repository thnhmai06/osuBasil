using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Content;

namespace Basil.Infrastructure.Storage.Beatmaps;

/// <summary>An .osz archive lying in the storage.</summary>
/// <param name="path">The archive's full path on disk.</param>
/// <param name="name">The archive's name in the storage.</param>
/// <param name="stamp">The archive's size and modification time.</param>
/// <param name="packedFrom">
///     The full path of the folder this archive was packed from, or <see langword="null" /> when it was not packed.
/// </param>
internal sealed class FileBeatmapsetArchive(string path, string name, ContentStamp stamp, string? packedFrom = null)
	: BeatmapsetArchive(name, stamp)
{
	/// <summary>Gets the archive's full path on disk.</summary>
	internal string Path => path;

	/// <summary>Gets the folder this archive was packed from, or <see langword="null" /> when it was not packed.</summary>
	internal string? PackedFrom => packedFrom;

	/// <inheritdoc />
	public override Task<Stream> OpenAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read,
			FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous));
	}
}

/// <summary>A folder of a beatmapset's files lying in the storage.</summary>
/// <param name="path">The folder's full path on disk.</param>
/// <param name="name">The folder's name in the storage.</param>
/// <param name="id">The beatmapset id the folder's name starts with, or <see langword="null" /> when it starts with none.</param>
/// <param name="exists">Whether the folder still exists.</param>
/// <param name="archive">The folder's own archive, or <see langword="null" /> when it holds none.</param>
internal sealed class FileBeatmapsetFolder(string path, string name, int? id, bool exists, BeatmapsetArchive? archive)
	: BeatmapsetFolder(name, id, exists, archive)
{
	/// <summary>Gets the folder's full path on disk.</summary>
	internal string Path => path;
}