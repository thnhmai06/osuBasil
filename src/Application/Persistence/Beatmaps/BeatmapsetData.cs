using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Content;

namespace Basil.Application.Persistence.Beatmaps;

/// <summary>A beatmapset as it is stored, without what only the store gives it: its id, times, lock and visibility.</summary>
/// <param name="OnlineId">
///     For a new beatmapset, its osu! id, which becomes its id; <see langword="null" /> for a new local
///     id.
/// </param>
/// <param name="Fingerprint">The fingerprint of the set's archive.</param>
/// <param name="Metadata">What the set's difficulties share.</param>
/// <param name="Assets">The files the set has besides its difficulties.</param>
/// <param name="Beatmaps">Every difficulty the set has.</param>
public sealed record BeatmapsetData(
	int? OnlineId,
	ContentFingerprint Fingerprint,
	BeatmapsetMetadata Metadata,
	BeatmapsetAssets Assets,
	IReadOnlyList<BeatmapData> Beatmaps);

/// <summary>A beatmap as it is stored, without what only the store gives it.</summary>
/// <param name="OnlineId">
///     For a new beatmap, its osu! id, which becomes its id; <see langword="null" /> for a new local
///     id.
/// </param>
/// <param name="Hash">The MD5 hash of its .osu file.</param>
/// <param name="Metadata">What the difficulty is.</param>
/// <param name="Assets">The files of its set the difficulty uses.</param>
public sealed record BeatmapData(int? OnlineId, Md5 Hash, BeatmapMetadata Metadata, BeatmapAssets Assets);