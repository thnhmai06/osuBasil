using Basil.Domain.Beatmaps;
using Basil.Domain.Content;
using Basil.Domain.Cryptography;

namespace Basil.Application.Beatmaps.Persistence;

public sealed record BeatmapsetData(
	int? OnlineId,
	ContentFingerprint Fingerprint,
	BeatmapsetMetadata Metadata,
	BeatmapsetAssets Assets,
	IReadOnlyList<BeatmapData> Beatmaps);

public sealed record BeatmapData(int? OnlineId, Md5 Hash, BeatmapMetadata Metadata, BeatmapAssets Assets);