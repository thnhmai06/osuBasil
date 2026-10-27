using System.IO.Compression;
using Basil.Application.Beatmaps.Persistence;

namespace Basil.Infrastructure.Beatmaps.Capabilities;

/// <summary>A file of a beatmapset read from an entry of its archive.</summary>
internal sealed class ZipBeatmapsetContentFile(ZipArchiveEntry entry, string name)
	: BeatmapsetContentFile(name, entry.Length)
{
	/// <inheritdoc />
	public override async Task<Stream> OpenAsync(CancellationToken cancellationToken = default)
	{
		return await entry.OpenAsync(cancellationToken);
	}
}