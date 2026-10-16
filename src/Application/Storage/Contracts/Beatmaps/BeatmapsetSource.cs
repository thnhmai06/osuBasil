using Basil.Domain.Common;
using Basil.Domain.Content;

namespace Basil.Application.Storage.Contracts.Beatmaps;

/// <summary>Raw beatmapset content lying in the storage: an .osz archive or a folder of a beatmapset's files.</summary>
/// <param name="name">The source's name in the storage.</param>
public abstract class BeatmapsetSource(string name)
{
	/// <summary>Gets the source's name in the storage.</summary>
	public string Name => name;
}

/// <summary>An .osz archive: one placed by hand, uploaded, packed from a folder, or a set folder's own.</summary>
/// <param name="name">The archive's name in the storage.</param>
/// <param name="stamp">The archive's size and modification time.</param>
public abstract class BeatmapsetArchive(string name, ContentStamp stamp) : BeatmapsetSource(name)
{
	/// <summary>Gets the archive's size and modification time, known without reading it.</summary>
	public ContentStamp Stamp => stamp;

	/// <summary>Opens the archive from its start.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>A new stream of the archive, which the caller disposes.</returns>
	public abstract Task<Stream> OpenAsync(CancellationToken cancellationToken = default);

	/// <summary>Reads the whole archive to take its fingerprint.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The archive's stamp with the MD5 hash of its bytes.</returns>
	public async Task<ContentFingerprint> FingerprintAsync(CancellationToken cancellationToken = default)
	{
		var stream = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using (stream.ConfigureAwait(false))
		{
			return new ContentFingerprint(stamp,
				await Md5.ComputeAsync(stream, cancellationToken).ConfigureAwait(false));
		}
	}
}

/// <summary>A folder directly in the storage: a set's folder, or a folder of a beatmapset's files placed by hand.</summary>
/// <param name="name">The folder's name in the storage.</param>
/// <param name="id">The beatmapset id the folder's name starts with, or <see langword="null" /> when it starts with none.</param>
/// <param name="exists">Whether the folder still exists.</param>
/// <param name="archive">The folder's own archive, or <see langword="null" /> when it holds none.</param>
public abstract class BeatmapsetFolder(string name, int? id, bool exists, BeatmapsetArchive? archive)
	: BeatmapsetSource(name)
{
	/// <summary>Gets the beatmapset id the folder's name starts with, or <see langword="null" /> when it starts with none.</summary>
	public int? Id => id;

	/// <summary>Gets a value that indicates whether the folder still exists.</summary>
	public bool Exists => exists;

	/// <summary>Gets the folder's own archive, or <see langword="null" /> when it holds none.</summary>
	/// <remarks>A folder that exists without an archive holds a beatmapset's files, which are packed into one first.</remarks>
	public BeatmapsetArchive? Archive => archive;
}