using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Rulesets.Ports;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Infrastructure.Beatmaps.Adapters;

internal sealed class BeatmapFiles(IBeatmapRepository beatmaps, IBeatmapsetStorage storage) : IBeatmapFiles
{
	public async ValueTask<Stream?> OpenAsync(int beatmapId, Md5Hash checksum, CancellationToken ct = default)
	{
		return await beatmaps.GetAsync(beatmapId, ct) is { } beatmap && beatmap.Hash == checksum
			? await storage.OpenAsync(beatmap.Beatmapset, BeatmapsetFileType.Version, $"{checksum}.osu", ct)
			: null;
	}
}