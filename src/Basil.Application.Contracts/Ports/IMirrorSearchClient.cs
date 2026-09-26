using Basil.Domain.Mechanics;

namespace Basil.Application.Contracts.Ports;

/// <summary>Queries a third-party osu!direct mirror for beatmapsets not known locally.</summary>
/// <remarks>A network failure is not surfaced as an exception; it is reported as an empty or null result.</remarks>
public interface IMirrorSearchClient
{
	/// <summary>Searches the mirror.</summary>
	/// <param name="search">The search criteria.</param>
	/// <param name="cancellationToken">A token that cancels the request.</param>
	/// <returns>The matching beatmapsets, or an empty list when the mirror errored or found nothing.</returns>
	Task<IReadOnlyList<MirrorBeatmapset>> SearchAsync(MirrorSearch search,
		CancellationToken cancellationToken = default);

	/// <summary>Gets a single beatmapset by its mirror-side id.</summary>
	/// <param name="setId">The id of the set on the mirror.</param>
	/// <param name="cancellationToken">A token that cancels the request.</param>
	/// <returns>The beatmapset, or <see langword="null" /> when it does not exist or the mirror errored.</returns>
	Task<MirrorBeatmapset?> GetSetAsync(int setId, CancellationToken cancellationToken = default);
}

/// <summary>The criteria for an <see cref="IMirrorSearchClient" /> search.</summary>
/// <param name="Query">The free-text query, or <see langword="null" /> for none.</param>
/// <param name="Mode">The game mode to filter by, or <see langword="null" /> for any mode.</param>
/// <param name="Amount">The maximum number of results to request.</param>
/// <param name="Offset">The zero-based result offset (page start).</param>
public sealed record MirrorSearch(string? Query, GameMode? Mode, int Amount, int Offset);

/// <summary>A beatmapset as returned by a mirror.</summary>
public sealed record MirrorBeatmapset(
	int SetId,
	string Artist,
	string Title,
	string Creator,
	DateTimeOffset LastUpdate,
	IReadOnlyList<MirrorBeatmap> Beatmaps);

/// <summary>A single difficulty within a <see cref="MirrorBeatmapset" />.</summary>
public sealed record MirrorBeatmap(string Version, double StarRating, GameMode Mode);