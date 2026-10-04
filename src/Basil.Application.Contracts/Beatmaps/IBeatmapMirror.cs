using Basil.Application.Storage.Common;
using Basil.Domain.Mechanics;

namespace Basil.Application.Contracts.Beatmaps;

/// <summary>Finds and serves beatmapsets the server does not have, from a beatmap mirror.</summary>
public interface IBeatmapMirror
{
	/// <summary>Searches the mirror.</summary>
	/// <param name="text">The search text, or <see langword="null" /> for any.</param>
	/// <param name="mode">The game mode, or <see langword="null" /> for any.</param>
	/// <param name="page">Which part of the results to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The matching beatmapsets, or <see langword="null" /> when no mirror is set or the mirror failed.</returns>
	Task<IReadOnlyList<MirrorBeatmapset>?> SearchAsync(string? text, GameMode? mode, PageRequest page, CancellationToken cancellationToken = default);

	/// <summary>Gets where a beatmapset can be downloaded from the mirror.</summary>
	/// <param name="beatmapsetId">The beatmapset id.</param>
	/// <param name="withVideo">Whether the download includes video files.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The download address, or <see langword="null" /> when no mirror is set.</returns>
	Task<Uri?> GetDownloadAddressAsync(int beatmapsetId, bool withVideo, CancellationToken cancellationToken = default);
}

/// <summary>A beatmapset as a mirror describes it.</summary>
/// <param name="Id">The beatmapset id.</param>
/// <param name="Artist">The artist.</param>
/// <param name="Title">The title.</param>
/// <param name="Creator">The mapper's name.</param>
/// <param name="UpdatedAt">When the beatmapset was last updated.</param>
/// <param name="HasVideo">Whether the beatmapset has video files.</param>
/// <param name="Beatmaps">The difficulties of the beatmapset.</param>
public sealed record MirrorBeatmapset(
	int Id,
	string Artist,
	string Title,
	string Creator,
	DateTimeOffset UpdatedAt,
	bool HasVideo,
	IReadOnlyList<MirrorBeatmap> Beatmaps);

/// <summary>One difficulty of a mirror beatmapset.</summary>
/// <param name="Id">The beatmap id.</param>
/// <param name="Version">The difficulty name.</param>
/// <param name="Mode">The game mode.</param>
/// <param name="StarRating">The star rating of the difficulty.</param>
public sealed record MirrorBeatmap(
	int Id,
	string Version,
	GameMode Mode,
	double StarRating);