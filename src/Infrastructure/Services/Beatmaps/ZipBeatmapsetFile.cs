using System.IO.Compression;
using Basil.Application.Storage.Contracts.Beatmaps;

namespace Basil.Infrastructure.Services.Beatmaps;

/// <summary>A file of a beatmapset read from an entry of its archive.</summary>
internal sealed class ZipBeatmapsetFile(ZipArchiveEntry entry, string name) : BeatmapsetFile(name, entry.Length)
{
	/// <inheritdoc />
	public override async Task<Stream> OpenAsync(CancellationToken cancellationToken = default)
	{
		return await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
	}
}
