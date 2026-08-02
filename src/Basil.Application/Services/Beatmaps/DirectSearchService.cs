using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Services.Beatmaps;

/// <summary>
///     Queries the local beatmap database for the osu!direct panel and formats the results.
/// </summary>
/// <remarks>
///     Queries the local beatmap database instead of proxying a mirror API, since this server runs
///     fully offline, and folds in the response formatting that both serving paths need. There is
///     no mirror-error result because this server never talks to a mirror. The metadata
///     pipe-replacement quirk is kept: it is not mirror-specific, it protects the pipe-delimited
///     wire format from any locally stored artist, title, or difficulty name that happens to
///     contain a literal <c>|</c>.
/// </remarks>
public sealed class DirectSearchService(IBeatmapRepository beatmaps, ILogger<DirectSearchService> logger)
{
	/// <summary>
	///     The number of results per page.
	/// </summary>
	/// <value>
	///     A full page signals to the client that more results may exist, reported as 101 rather
	///     than the literal count.
	/// </value>
	private const int PageSize = 100;

	/// <summary>Client sentinel for "any mode" in <see cref="DirectSearchRequest.Mode" />.</summary>
	private const int AnyMode = -1;

	// ASP.NET Core model binding decodes a query string's literal "+" as a space (matching
	// x-www-form-urlencoded rules) before this ever sees it, so the sentinel here must be the
	// decoded form ("Top Rated"): a literal "+" would never match what the client actually sent.
	private static readonly string[] NonTextQueries = ["Newest", "Top Rated", "Most Played"];

	/// <summary>
	///     Runs a single osu!direct search against the local beatmap database.
	/// </summary>
	/// <param name="request">The search parameters.</param>
	/// <param name="cancellationToken">A token that cancels the search.</param>
	/// <returns>The matching beatmapsets, each as a list of its difficulties.</returns>
	/// <remarks>
	///     The non-text query names and the any-mode sentinel are mapped to a null text filter and
	///     a null mode filter before the repository is queried.
	/// </remarks>
	public async Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(
		DirectSearchRequest request, CancellationToken cancellationToken = default)
	{
		var queryText = NonTextQueries.Contains(request.Query) ? null : request.Query;
		GameMode? mode = request.Mode == AnyMode ? null : (GameMode)request.Mode;

		var results = await beatmaps.SearchAsync(queryText, mode, request.PageNum * PageSize, PageSize,
			cancellationToken);
		logger.LogDebug("osu!direct search: Query={Query} Mode={Mode} PageNum={PageNum} ResultCount={ResultCount}",
			queryText, mode, request.PageNum, results.Count);
		return results;
	}

	/// <summary>
	///     Formats a set of search results into the osu!direct response format.
	/// </summary>
	/// <param name="beatmapSets">The search results, each entry a list of one set's difficulties.</param>
	/// <returns>The newline- and pipe-delimited response string.</returns>
	/// <remarks>
	///     A full page of results is reported as 101 rather than the literal count, which signals to
	///     the client that more pages may exist. Pipes in set metadata are replaced so the delimited
	///     format stays intact.
	/// </remarks>
	public static string Format(IReadOnlyList<IReadOnlyList<Beatmap>> beatmapSets)
	{
		var resultCount = beatmapSets.Count == PageSize ? 101 : beatmapSets.Count;
		var lines = new List<string> { resultCount.ToString() };
		lines.AddRange(from set in beatmapSets
			let first = set[0]
			let diffs = string.Join(",", set.Select(FormatDiff))
			select string.Join('|', $"{first.Beatmapset.Id}.osz", RemovePipes(first.Beatmapset.Artist),
				RemovePipes(first.Beatmapset.Title), first.Beatmapset.Creator,
				Beatmapset.Status.ToOsuApi().ToString(), "10.0",
				first.Beatmapset.LastUpdate.ToString("yyyy-MM-dd HH:mm:ss"), first.Beatmapset.Id.ToString(), "0", "0", "0", "0",
				"0", diffs));

		return string.Join("\n", lines);
	}

	/// <summary>
	///     Formats a single beatmapset into the osu!direct response format.
	/// </summary>
	/// <param name="beatmapSet">The set to format, given as one of its beatmaps, or <see langword="null" />.</param>
	/// <returns>
	///     The pipe-delimited response string, or an empty string when <paramref name="beatmapSet" /> is
	///     <see langword="null" />.
	/// </returns>
	/// <remarks>
	///     Unlike <see cref="Format" />, this does not escape pipes in metadata and reports the
	///     beatmap status as the server's own raw enum value rather than the converted osu!api
	///     value. The two endpoints therefore format the status differently, a known inconsistency
	///     that is preserved on purpose.
	/// </remarks>
	public static string FormatSet(Beatmap? beatmapSet)
	{
		if (beatmapSet is null) return "";

		return string.Join('|',
			$"{beatmapSet.Beatmapset.Id}.osz",
			beatmapSet.Beatmapset.Artist,
			beatmapSet.Beatmapset.Title,
			beatmapSet.Beatmapset.Creator,
			((int)Beatmapset.Status).ToString(),
			"10.0",
			beatmapSet.Beatmapset.LastUpdate.ToString("yyyy-MM-dd HH:mm:ss"),
			beatmapSet.Beatmapset.Id.ToString(),
			"0", "0", "0", "0", "0");
	}

	private static string FormatDiff(Beatmap beatmap)
	{
		return $"[{beatmap.Difficulty.Sr:0.00}⭐] {RemovePipes(beatmap.Version)} " +
		       $"{{CS: {beatmap.Difficulty.Cs} / OD: {beatmap.Difficulty.Od} / AR: {beatmap.Difficulty.Ar} / " +
		       $"HP: {beatmap.Difficulty.Hp}}}@{(int)beatmap.Difficulty.Mode}";
	}

	// "|" is the field delimiter in this response format, so any literal "|" in metadata would corrupt it.
	private static string RemovePipes(string value)
	{
		return value.Replace('|', 'I');
	}
}

/// <summary>
///     The parameters of a single osu!direct search.
/// </summary>
/// <param name="Query">
///     The search text, or one of the non-text query names (<c>Newest</c>, <c>Top Rated</c>,
///     <c>Most Played</c>).
/// </param>
/// <param name="Mode">The game mode to filter by, or <c>-1</c> for any mode.</param>
/// <param name="PageNum">The zero-based page of results to return.</param>
public sealed record DirectSearchRequest(string Query, int Mode, int PageNum);