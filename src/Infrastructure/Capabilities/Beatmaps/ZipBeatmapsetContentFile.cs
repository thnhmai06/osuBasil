using System.IO.Compression;
using Basil.Application.Storage.Beatmaps;

namespace Basil.Infrastructure.Capabilities.Beatmaps;

/// <summary>A file of a beatmapset read from an entry of its archive.</summary>
internal sealed class ZipBeatmapsetContentFile(ZipArchiveEntry entry, string name)
	: BeatmapsetContentFile(name, entry.Length)
{
	/// <inheritdoc />
	public override async Task<Stream> OpenAsync(CancellationToken cancellationToken = default)
	{
		return await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
	}
}