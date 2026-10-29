using System.Linq.Expressions;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Common.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Beatmaps.Persistence;

/// <summary>Narrows the stored beatmaps to the ones a beatmap query asks for.</summary>
internal static class BeatmapQueryFilter
{
	/// <summary>Applies the conditions of a beatmap query to the stored beatmaps.</summary>
	/// <param name="beatmaps">The stored beatmaps, with their sets reachable through <see cref="BeatmapRow.Set" />.</param>
	/// <param name="query">The query to filter by.</param>
	/// <returns>The beatmaps that match every condition of the query.</returns>
	public static IQueryable<BeatmapRow> Apply(IQueryable<BeatmapRow> beatmaps, BeatmapQuery query)
	{
		beatmaps = ByState(beatmaps, query);
		beatmaps = ByRanges(beatmaps, query);
		beatmaps = ByFreeText(beatmaps, query);
		beatmaps = ByNames(beatmaps, query);
		return ByVersionAndFileName(beatmaps, query);
	}

	/// <summary>Keeps the beatmaps in the asked status, visibility and mode.</summary>
	private static IQueryable<BeatmapRow> ByState(IQueryable<BeatmapRow> beatmaps, BeatmapQuery query)
	{
		if (query.Status is { } status)
			beatmaps = beatmaps.Where(row => row.Set.Status == status);

		if (query.Hidden is { } hidden)
			beatmaps = hidden
				? beatmaps.Where(row => !(row.Visible && row.Set.Visible))
				: beatmaps.Where(row => row.Visible && row.Set.Visible);

		if (query.Mode is { } mode)
		{
			var code = (int)mode;
			beatmaps = beatmaps.Where(row => row.Mode == code);
		}

		return beatmaps;
	}

	/// <summary>Keeps the beatmaps whose numbers and dates lie within the asked intervals.</summary>
	private static IQueryable<BeatmapRow> ByRanges(IQueryable<BeatmapRow> beatmaps, BeatmapQuery query)
	{
		beatmaps = Within(beatmaps, row => row.Star, query.Stars);
		beatmaps = Within(beatmaps, row => row.Ar, query.Ar);
		beatmaps = Within(beatmaps, row => row.Cs, query.Cs);
		beatmaps = Within(beatmaps, row => row.Od, query.Od);
		beatmaps = Within(beatmaps, row => row.Hp, query.Hp);
		beatmaps = Within(beatmaps, row => row.Bpm, query.Bpm);

		// Length: the query is in seconds, the column stores milliseconds.
		beatmaps = Within(beatmaps, row => row.Length / 1000.0, query.Length);
		beatmaps = Within(beatmaps, row => row.Objects.GetProperty("Circles").GetInt32(), query.Circles);
		beatmaps = Within(beatmaps, row => row.Objects.GetProperty("Sliders").GetInt32(), query.Sliders);

		// Created at / Updated at: the query bounds are UTC instants.
		beatmaps = Within(beatmaps, row => row.Set.CreatedAt, query.Created,
			static instant => instant.ToUniversalTime());
		beatmaps = Within(beatmaps, row => row.Set.UpdatedAt, query.Updated,
			static instant => instant.ToUniversalTime());

		return beatmaps;
	}

	/// <summary>Keeps the beatmaps that match every word of the free text.</summary>
	private static IQueryable<BeatmapRow> ByFreeText(IQueryable<BeatmapRow> beatmaps, BeatmapQuery query)
	{
		// Free text: every whitespace-separated word must match artist, title, creator or version.
		if (!string.IsNullOrWhiteSpace(query.Text))
			beatmaps = query.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(LikePattern.Containing).Aggregate(beatmaps, (current, pattern)
					=> current.Where(row =>
						EF.Functions.ILike(row.Set.Artist, pattern, LikePattern.Escape) ||
						EF.Functions.ILike(row.Set.Title, pattern, LikePattern.Escape) ||
						EF.Functions.ILike(row.Set.Creator, pattern, LikePattern.Escape) ||
						EF.Functions.ILike(row.Version, pattern, LikePattern.Escape)));

		return beatmaps;
	}

	/// <summary>Keeps the beatmaps whose creator, artist and title contain the asked text.</summary>
	private static IQueryable<BeatmapRow> ByNames(IQueryable<BeatmapRow> beatmaps, BeatmapQuery query)
	{
		if (!string.IsNullOrWhiteSpace(query.Creator))
		{
			var pattern = LikePattern.Containing(query.Creator);
			beatmaps = beatmaps.Where(row => EF.Functions.ILike(row.Set.Creator, pattern, LikePattern.Escape));
		}

		if (!string.IsNullOrWhiteSpace(query.Artist))
		{
			var pattern = LikePattern.Containing(query.Artist);
			beatmaps = beatmaps.Where(row => EF.Functions.ILike(row.Set.Artist, pattern, LikePattern.Escape));
		}

		if (!string.IsNullOrWhiteSpace(query.Title))
		{
			var pattern = LikePattern.Containing(query.Title);
			beatmaps = beatmaps.Where(row => EF.Functions.ILike(row.Set.Title, pattern, LikePattern.Escape));
		}

		return beatmaps;
	}

	/// <summary>Keeps the beatmaps with the asked difficulty name and file name.</summary>
	private static IQueryable<BeatmapRow> ByVersionAndFileName(IQueryable<BeatmapRow> beatmaps, BeatmapQuery query)
	{
		if (!string.IsNullOrWhiteSpace(query.Difficulty))
		{
			var pattern = LikePattern.Containing(query.Difficulty);
			beatmaps = beatmaps.Where(row => EF.Functions.ILike(row.Version, pattern, LikePattern.Escape));
		}

		if (!string.IsNullOrWhiteSpace(query.FileName))
		{
			var pattern = LikePattern.Literal(BeatmapMetadata.NormalizeOsuFileName(query.FileName));
			// The name of the .osu file of a beatmap leaves out the characters a file name cannot hold.
			beatmaps = beatmaps.Where(row => EF.Functions.ILike(
				(row.Set.Artist + " - " + row.Set.Title + " (" + row.Set.Creator + ") [" + row.Version + "].osu")
				.Replace(":", "").Replace("\\", "").Replace("/", "").Replace("*", "").Replace("<", "").Replace(">", "")
				.Replace("?", "").Replace("\"", "").Replace("|", ""),
				pattern, LikePattern.Escape));
		}

		return beatmaps;
	}

	/// <summary>Keeps the beatmaps whose selected value lies within an interval, when the query gives one.</summary>
	private static IQueryable<BeatmapRow> Within<T>(IQueryable<BeatmapRow> beatmaps,
		Expression<Func<BeatmapRow, T>> selector, Interval<T>? interval, Func<T, T>? convert = null)
		where T : struct, IComparable<T>
	{
		if (interval is not { } bounds)
			return beatmaps;

		if (bounds.Min is { } min)
			beatmaps = beatmaps.Where(Compare(selector, convert?.Invoke(min) ?? min,
				bounds.MinInclusive ? ExpressionType.GreaterThanOrEqual : ExpressionType.GreaterThan));
		if (bounds.Max is { } max)
			beatmaps = beatmaps.Where(Compare(selector, convert?.Invoke(max) ?? max,
				bounds.MaxInclusive ? ExpressionType.LessThanOrEqual : ExpressionType.LessThan));
		return beatmaps;
	}

	/// <summary>Builds the condition that compares the selected value with a bound.</summary>
	private static Expression<Func<BeatmapRow, bool>> Compare<T>(Expression<Func<BeatmapRow, T>> selector, T bound,
		ExpressionType comparison)
	{
		var body = Expression.MakeBinary(comparison, selector.Body, Expression.Constant(bound, typeof(T)));
		return Expression.Lambda<Func<BeatmapRow, bool>>(body, selector.Parameters);
	}
}