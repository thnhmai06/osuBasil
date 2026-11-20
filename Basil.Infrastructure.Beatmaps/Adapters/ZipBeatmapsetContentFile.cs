using System.IO.Compression;
using Basil.Application.Beatmaps.Persistence;

namespace Basil.Infrastructure.Beatmaps.Adapters;

internal sealed class ZipBeatmapsetContentFile(ZipArchiveEntry entry, string name)
	: BeatmapsetContentFile(name, entry.Length)
{
	public override async Task<Stream> OpenAsync(CancellationToken ct = default)
	{
		return await entry.OpenAsync(ct);
	}
}