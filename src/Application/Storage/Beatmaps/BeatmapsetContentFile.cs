using Basil.Domain.Common;

namespace Basil.Application.Storage.Beatmaps;

/// <summary>
///     One file inside a beatmapset, such as a difficulty, a background or an audio track, opened anew each time it
///     is read.
/// </summary>
/// <param name="name">The file's name relative to the set, with '/' between folders.</param>
/// <param name="length">The file's size in bytes.</param>
public abstract class BeatmapsetContentFile(string name, long length)
{
	/// <summary>Gets the file's name relative to the set, with '/' between folders.</summary>
	public string Name => name;

	/// <summary>Gets the file's size in bytes.</summary>
	public long Length => length;

	/// <summary>Opens the file's content from its start.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>A new stream of the content, which the caller disposes.</returns>
	/// <remarks>Files of one beatmapset are read one at a time.</remarks>
	public abstract Task<Stream> OpenAsync(CancellationToken cancellationToken = default);

	/// <summary>Computes the MD5 hash of the file's content.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The MD5 hash of the content, read to its end.</returns>
	public async Task<Md5> HashAsync(CancellationToken cancellationToken = default)
	{
		var stream = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using (stream.ConfigureAwait(false))
		{
			return await Md5.ComputeAsync(stream, cancellationToken).ConfigureAwait(false);
		}
	}
}