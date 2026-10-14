namespace Basil.Application.Storage.Contracts.Beatmaps;

/// <summary>Something lying directly in the beatmapset storage: an .osz archive or a folder.</summary>
/// <param name="name">The entry's name in the storage.</param>
public abstract class BeatmapsetEntry(string name)
{
	/// <summary>Gets the entry's name in the storage.</summary>
	public string Name => name;
}

/// <summary>An .osz archive: one placed in the storage by hand, or a set folder's own archive.</summary>
/// <param name="name">The archive's name in the storage.</param>
/// <param name="size">The archive's size in bytes.</param>
/// <param name="modifiedAt">When the archive was last modified, in UTC.</param>
public abstract class BeatmapsetArchiveEntry(string name, long size, DateTimeOffset modifiedAt) : BeatmapsetEntry(name)
{
	/// <summary>Gets the archive's size in bytes.</summary>
	public long Size => size;

	/// <summary>Gets when the archive was last modified, in UTC.</summary>
	public DateTimeOffset ModifiedAt => modifiedAt;

	/// <summary>Opens the archive from its start.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>A new stream of the archive, which the caller disposes.</returns>
	public abstract Task<Stream> OpenAsync(CancellationToken cancellationToken = default);
}

/// <summary>A folder directly in the storage: a set's folder, or a folder of loose files placed by hand.</summary>
/// <param name="name">The folder's name in the storage.</param>
/// <param name="id">The beatmapset id the folder's name starts with, or <see langword="null" /> when it starts with none.</param>
/// <param name="exists">Whether the folder still exists.</param>
/// <param name="archive">The folder's own archive, or <see langword="null" /> when it holds none.</param>
public abstract class BeatmapsetFolderEntry(string name, int? id, bool exists, BeatmapsetArchiveEntry? archive)
	: BeatmapsetEntry(name)
{
	/// <summary>Gets the beatmapset id the folder's name starts with, or <see langword="null" /> when it starts with none.</summary>
	public int? Id => id;

	/// <summary>Gets a value that indicates whether the folder still exists.</summary>
	public bool Exists => exists;

	/// <summary>Gets the folder's own archive, or <see langword="null" /> when it holds none.</summary>
	/// <remarks>A folder that exists without an archive holds loose files, which can be packed into one.</remarks>
	public BeatmapsetArchiveEntry? Archive => archive;
}