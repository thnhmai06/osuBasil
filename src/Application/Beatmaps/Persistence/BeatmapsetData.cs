using Basil.Domain.Beatmaps;
using Basil.Domain.Content;
using Basil.Shared.Structures.Cryptography;


namespace Basil.Application.Beatmaps.Persistence;

public sealed record BeatmapsetData(
	int? OnlineId,
	ContentIdentifier Fingerprint,
	BeatmapsetMetadata Metadata,
	BeatmapsetAssets Assets,
	IReadOnlyList<BeatmapData> Beatmaps);

public sealed record BeatmapData(int? OnlineId, Md5Hash Hash, BeatmapMetadata Metadata, BeatmapAssets Assets);