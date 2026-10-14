using Basil.Application.Storage.Contracts.Beatmaps;

namespace Basil.Infrastructure.Storage.Beatmaps;

/// <summary>An .osz archive lying in the storage, waiting to be taken in as a set's archive.</summary>
/// <param name="path">The archive's full path on disk.</param>
/// <param name="name">The archive's name in the storage.</param>
/// <param name="size">The archive's size in bytes.</param>
/// <param name="modifiedAt">When the archive was last modified, in UTC.</param>
/// <param name="packedFrom">
///     The full path of the folder of loose files this archive was packed from, or <see langword="null" /> when it was
///     not packed from loose files.
/// </param>
internal sealed class FileBeatmapsetArchive(string path, string name, long size, DateTimeOffset modifiedAt,
	string? packedFrom = null) : BeatmapsetArchiveEntry(name, size, modifiedAt)
{
	/// <summary>Gets the archive's full path on disk.</summary>
	internal string Path => path;

	/// <summary>Gets the folder of loose files this archive was packed from, or <see langword="null" /> when none.</summary>
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
internal sealed class FileBeatmapsetFolder(string path, string name, int? id, bool exists,
	BeatmapsetArchiveEntry? archive) : BeatmapsetFolderEntry(name, id, exists, archive)
{
	/// <summary>Gets the folder's full path on disk.</summary>
	internal string Path => path;
}

/// <summary>An upload held in its own folder of the storage while its beatmapsets are taken in.</summary>
/// <param name="path">The upload folder's full path on disk.</param>
internal sealed class FileBeatmapsetUpload(string path) : BeatmapsetUpload
{
	/// <summary>Gets the upload folder's full path on disk.</summary>
	internal string Path => path;

	/// <inheritdoc />
	public override IReadOnlyList<BeatmapsetArchiveEntry> Archives => MutableArchives;

	/// <summary>Gets the archives found in the upload, to add to while it is looked through.</summary>
	internal List<BeatmapsetArchiveEntry> MutableArchives { get; } = [];
}
