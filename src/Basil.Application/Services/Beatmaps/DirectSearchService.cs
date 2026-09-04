using System.Collections.Frozen;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Services.Beatmaps;

/// <summary>
///     Queries the local beatmap database or a configured mirror for the osu!direct panel and
///     formats the results.
/// </summary>
/// <remarks>
///     <see cref="SearchAsync" />/<see cref="Format" /> query the local beatmap database only, kept
///     exactly as before mirror search existed. <see cref="SearchFormattedAsync" /> is the
///     orchestrator every route should call instead: it queries the mirror when
///     <see cref="MirrorEndpoints.HasSearchMirror" /> is set (falling back to local on mirror
///     failure), or local otherwise. The metadata pipe-replacement quirk applies to both sources: it
///     protects the pipe-delimited wire format from any artist, title, or difficulty name that
///     happens to contain a literal <c>|</c>.
/// </remarks>
public sealed class DirectSearchService(
	IBeatmapRepository beatmaps,
	IMirrorSearchClient mirrorSearchClient,
	MirrorService mirrorService,
	ILogger<DirectSearchService> logger)
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
	private static readonly FrozenSet<string> NonTextQueries =
		new[] { "Newest", "Top Rated", "Most Played" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

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
		var filters = NonTextQueries.Contains(request.Query)
			? BeatmapsetSearchFilters.Empty
			: BeatmapsetSearchQueryParser.Parse(request.Query);
		GameMode? mode = request.Mode == AnyMode ? null : (GameMode)request.Mode;

		var results = await beatmaps.SearchAsync(filters, mode, request.PageNum * PageSize, PageSize,
			cancellationToken);
		logger.LogDebug("osu!direct search: Query={Query} Mode={Mode} PageNum={PageNum} ResultCount={ResultCount}",
			request.Query, mode, request.PageNum, results.Count);
		return results;
	}

	/// <summary>
	///     Runs a single osu!direct search and formats the response, querying the configured mirror
	///     instead of local storage when one is set.
	/// </summary>
	/// <param name="request">The search parameters.</param>
	/// <param name="cancellationToken">A token that cancels the search.</param>
	/// <returns>The newline- and pipe-delimited response string.</returns>
	/// <remarks>
	///     When a search mirror is configured, it replaces local search entirely rather than only
	///     filling in when local search returns nothing: a search result list isn't a single resource
	///     like a thumbnail, so a local match would otherwise silently hide the rest of the mirror's
	///     catalog. A mirror that errors or is unreachable falls back to local search for that call,
	///     so a temporary mirror outage doesn't take search down entirely.
	/// </remarks>
	public async Task<string> SearchFormattedAsync(DirectSearchRequest request,
		CancellationToken cancellationToken = default)
	{
		var mirror = await mirrorService.GetAsync(cancellationToken);
		if (mirror.HasSearchMirror)
		{
			var queryText = NonTextQueries.Contains(request.Query) ? null : request.Query;
			GameMode? mode = request.Mode == AnyMode ? null : (GameMode)request.Mode;

			var mirrorResults = await mirrorSearchClient.SearchAsync(mirror.SearchEndpoint!, queryText, (int?)mode,
				PageSize, request.PageNum * PageSize, cancellationToken);
			if (mirrorResults is not null)
			{
				logger.LogDebug(
					"osu!direct mirror search: Query={Query} Mode={Mode} PageNum={PageNum} ResultCount={ResultCount}",
					queryText, mode, request.PageNum, mirrorResults.Count);
				return FormatMirror(mirrorResults);
			}

			logger.LogWarning("osu!direct mirror search failed, falling back to local search: Endpoint={Endpoint}",
				mirror.SearchEndpoint);
		}

		return Format(await SearchAsync(request, cancellationToken));
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
				first.Beatmapset.LastUpdate.ToString("yyyy-MM-dd HH:mm:ss"), first.Beatmapset.Id.ToString(), "0", "0",
				"0", "0",
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

	/// <summary>
	///     Formats a set of mirror search results into the osu!direct response format, matching
	///     <see cref="Format" />'s layout but reading directly from the mirror's own DTOs (which don't
	///     carry every field a local <see cref="Beatmap" /> does, e.g. star rating source or filenames).
	/// </summary>
	/// <param name="sets">The mirror's search results.</param>
	/// <returns>The newline- and pipe-delimited response string.</returns>
	public static string FormatMirror(IReadOnlyList<MirrorSearchSet> sets)
	{
		var resultCount = sets.Count == PageSize ? 101 : sets.Count;
		var lines = new List<string> { resultCount.ToString() };
		lines.AddRange(sets.Select(set =>
		{
			var diffs = set.Beatmaps is null ? "" : string.Join(",", set.Beatmaps.Select(FormatMirrorDiff));
			return string.Join('|', $"{set.SetId}.osz", RemovePipes(set.Artist), RemovePipes(set.Title),
				set.Creator, set.RankedStatus.ToString(), "10.0", set.LastUpdate, set.SetId.ToString(),
				set.HasVideo ? "1" : "0", "0", "0", "0", "0", diffs);
		}));

		return string.Join("\n", lines);
	}

	private static string FormatMirrorDiff(MirrorSearchBeatmap beatmap)
	{
		return $"[{beatmap.DifficultyRating:0.00}⭐] {RemovePipes(beatmap.DiffName)} " +
		       $"{{CS: {beatmap.Cs} / OD: {beatmap.Od} / AR: {beatmap.Ar} / HP: {beatmap.Hp}}}@{beatmap.Mode}";
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