using System.Linq.Expressions;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Beatmaps.Persistence;

internal static class BeatmapQueryFilter
{
	public static IQueryable<BeatmapRow> Apply(IQueryable<BeatmapRow> beatmaps, BeatmapQuery query)
	{
		beatmaps = ByState(beatmaps, query);
		beatmaps = ByRanges(beatmaps, query);
		beatmaps = ByFreeText(beatmaps, query);
		beatmaps = ByNames(beatmaps, query);
		return ByVersionAndFileName(beatmaps, query);
	}

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

	private static IQueryable<BeatmapRow> ByRanges(IQueryable<BeatmapRow> beatmaps, BeatmapQuery query)
	{
		// A beatmap whose difficulty is not calculated yet matches none of these.
		if (query.Stars is not null)
			beatmaps = Within(beatmaps.Where(row => row.Star != null), row => row.Star!.Value, query.Stars);
		if (query.Ar is not null)
			beatmaps = Within(beatmaps.Where(row => row.Ar != null), row => row.Ar!.Value, query.Ar);
		if (query.Cs is not null)
			beatmaps = Within(beatmaps.Where(row => row.Cs != null), row => row.Cs!.Value, query.Cs);
		if (query.Od is not null)
			beatmaps = Within(beatmaps.Where(row => row.Od != null), row => row.Od!.Value, query.Od);
		if (query.Hp is not null)
			beatmaps = Within(beatmaps.Where(row => row.Hp != null), row => row.Hp!.Value, query.Hp);
		if (query.Bpm is not null)
			beatmaps = Within(beatmaps.Where(row => row.Bpm != null), row => row.Bpm!.Value, query.Bpm);

		// Length: the query is in seconds, the column stores milliseconds.
		if (query.Length is not null)
			beatmaps = Within(beatmaps.Where(row => row.Length != null), row => row.Length!.Value / 1000.0,
				query.Length);
		// Only standard beatmaps count circles and sliders.
		if (query.Circles is not null)
			beatmaps = Within(beatmaps.Where(row => row.Circles != null), row => row.Circles!.Value, query.Circles);
		if (query.Sliders is not null)
			beatmaps = Within(beatmaps.Where(row => row.Sliders != null), row => row.Sliders!.Value, query.Sliders);

		// Created at / Updated at: the query bounds are UTC instants.
		beatmaps = Within(beatmaps, row => row.Set.CreatedAt, query.Created,
			static instant => instant.ToUniversalTime());
		beatmaps = Within(beatmaps, row => row.Set.UpdatedAt, query.Updated,
			static instant => instant.ToUniversalTime());

		return beatmaps;
	}

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

	private static IQueryable<BeatmapRow> ByVersionAndFileName(IQueryable<BeatmapRow> beatmaps, BeatmapQuery query)
	{
		if (!string.IsNullOrWhiteSpace(query.Difficulty))
		{
			var pattern = LikePattern.Containing(query.Difficulty);
			beatmaps = beatmaps.Where(row => EF.Functions.ILike(row.Version, pattern, LikePattern.Escape));
		}

		if (!string.IsNullOrWhiteSpace(query.FileName))
		{
			var fileName = BeatmapFileName.Normalize(query.FileName);
			beatmaps = beatmaps.Where(row => row.FileName == fileName);
		}

		return beatmaps;
	}

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

	private static Expression<Func<BeatmapRow, bool>> Compare<T>(Expression<Func<BeatmapRow, T>> selector, T bound,
		ExpressionType comparison)
	{
		var body = Expression.MakeBinary(comparison, selector.Body, Expression.Constant(bound, typeof(T)));
		return Expression.Lambda<Func<BeatmapRow, bool>>(body, selector.Parameters);
	}
}