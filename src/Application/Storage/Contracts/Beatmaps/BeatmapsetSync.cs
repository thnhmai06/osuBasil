using Basil.Domain.Beatmaps;
using Basil.Domain.Utilities;

namespace Basil.Application.Storage.Contracts.Beatmaps;

/// <summary>What one scan or import brings in step: the beatmapsets to store as read from their archives, and those to remove.</summary>
/// <param name="Store">The beatmapsets to create or replace, each with every difficulty it now has.</param>
/// <param name="Remove">The stored beatmapsets to remove with their beatmaps.</param>
public sealed record BeatmapsetSync(IReadOnlyList<BeatmapsetSyncSet> Store, IReadOnlyCollection<Beatmapset> Remove);

/// <summary>One beatmapset as read from its archive.</summary>
/// <param name="Existing">The stored beatmapset it replaces, or <see langword="null" /> to create a new one.</param>
/// <param name="OnlineId">For a new beatmapset, its osu! id, which becomes its id; <see langword="null" /> for a new local id.</param>
/// <param name="Artist">The artist.</param>
/// <param name="Title">The title.</param>
/// <param name="Creator">The mapper's name.</param>
/// <param name="ArchiveHash">The MD5 hash of the archive.</param>
/// <param name="ArchiveSize">The archive's size in bytes.</param>
/// <param name="ArchiveModifiedAt">When the archive was last modified, in UTC.</param>
/// <param name="Storyboard">Whether the set has a storyboard.</param>
/// <param name="Beatmaps">Every difficulty the set now has; stored difficulties not listed are removed.</param>
public sealed record BeatmapsetSyncSet(
	Beatmapset? Existing,
	int? OnlineId,
	string Artist,
	string Title,
	string Creator,
	Md5 ArchiveHash,
	long ArchiveSize,
	DateTimeOffset ArchiveModifiedAt,
	bool Storyboard,
	IReadOnlyList<BeatmapSyncData> Beatmaps);

/// <summary>One difficulty as read from its .osu file.</summary>
/// <param name="OnlineId">Its osu! id, which becomes the id of a new beatmap; <see langword="null" /> for a new local id.</param>
/// <param name="Hash">The MD5 hash of its .osu file.</param>
/// <param name="Version">The difficulty name.</param>
/// <param name="Difficulty">Its gameplay stats without mods.</param>
/// <param name="Objects">Its per-mode hit-object counts.</param>
/// <param name="Background">The name its background is stored under, or <see langword="null" />.</param>
/// <param name="Audio">The name its audio track is stored under, or <see langword="null" />.</param>
/// <param name="Video">The name its video is stored under, or <see langword="null" />.</param>
/// <param name="PreviewTime">Where the audio preview starts, or <see langword="null" />.</param>
public sealed record BeatmapSyncData(
	int? OnlineId,
	Md5 Hash,
	string Version,
	Difficulty Difficulty,
	BeatmapObjects Objects,
	string? Background,
	string? Audio,
	string? Video,
	TimeSpan? PreviewTime);
