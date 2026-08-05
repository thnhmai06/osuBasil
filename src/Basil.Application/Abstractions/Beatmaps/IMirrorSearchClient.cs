namespace Basil.Application.Abstractions.Beatmaps;

/// <summary>
///     Queries an external osu!direct mirror's search API.
/// </summary>
public interface IMirrorSearchClient
{
	/// <summary>Runs a search against the given mirror endpoint.</summary>
	/// <param name="endpoint">The mirror's search API base URL.</param>
	/// <param name="query">The free-text query, or <see langword="null" /> for none.</param>
	/// <param name="mode">The game mode to filter by, or <see langword="null" /> for any mode.</param>
	/// <param name="amount">The maximum number of results to request.</param>
	/// <param name="offset">The zero-based result offset (page start).</param>
	/// <param name="cancellationToken">A token that cancels the request.</param>
	/// <returns>The matching beatmapsets, or <see langword="null" /> if the mirror errored or is unreachable.</returns>
	Task<IReadOnlyList<MirrorSearchSet>?> SearchAsync(string endpoint, string? query, int? mode, int amount,
		int offset, CancellationToken cancellationToken = default);
}

/// <summary>A beatmapset as returned by a mirror's search API.</summary>
public sealed record MirrorSearchSet(
	string Artist,
	string Title,
	string Creator,
	int RankedStatus,
	string LastUpdate,
	int SetId,
	bool HasVideo,
	IReadOnlyList<MirrorSearchBeatmap>? Beatmaps);

/// <summary>A single difficulty within a <see cref="MirrorSearchSet" />.</summary>
public sealed record MirrorSearchBeatmap(
	double DifficultyRating,
	string DiffName,
	double Cs,
	double Od,
	double Ar,
	double Hp,
	int Mode);