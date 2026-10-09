using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Beatmaps;
using Dapper;

namespace Basil.Infrastructure.Storage.Beatmaps;

/// <summary>Builds the WHERE clause and parameters for beatmap queries.</summary>
internal static class BeatmapQueryFilter
{
	/// <summary>Appends the filter conditions for a beatmap query to the WHERE clause.</summary>
	/// <param name="query">The query to filter by.</param>
	/// <param name="parameters">The parameters to add the values to.</param>
	/// <returns>The SQL WHERE clause fragment (without the WHERE keyword), or empty when no filter applies.</returns>
	/// <remarks>
	///     The caller aliases the beatmap table as <c>b</c> and the beatmapset table as <c>s</c>.
	///     <see cref="Domain.Beatmaps.Beatmapset.Status" /> is a static member that always returns
	///     <see cref="Domain.Beatmaps.BeatmapStatus.Approved" />; when the query asks for a different
	///     status, the returned fragment matches no row.
	/// </remarks>
	public static string Build(BeatmapQuery query, DynamicParameters parameters)
	{
		var conditions = new List<string>();

		if (!query.IncludeHidden)
		{
			conditions.Add("b.visible");
			conditions.Add("s.visible");
		}

		if (query.Mode is { } mode)
		{
			parameters.Add("mode", (int)mode);
			conditions.Add("b.mode = @mode");
		}

		if (query.Stars is { } stars)
			AddInterval(conditions, parameters, "b.star", stars, "starsMin", "starsMax");

		if (query.Ar is { } ar)
			AddInterval(conditions, parameters, "b.ar", ar, "arMin", "arMax");

		if (query.Cs is { } cs)
			AddInterval(conditions, parameters, "b.cs", cs, "csMin", "csMax");

		if (query.Od is { } od)
			AddInterval(conditions, parameters, "b.od", od, "odMin", "odMax");

		if (query.Hp is { } hp)
			AddInterval(conditions, parameters, "b.hp", hp, "hpMin", "hpMax");

		if (query.Bpm is { } bpm)
			AddInterval(conditions, parameters, "b.bpm", bpm, "bpmMin", "bpmMax");

		// Length: the query is in seconds, the column stores milliseconds.
		if (query.Length is { } length)
			AddInterval(conditions, parameters, "b.length / 1000.0", length, "lengthMin", "lengthMax");

		if (query.Circles is { } circles)
			AddInterval(conditions, parameters, "(b.objects->>'Circles')::int", circles, "circlesMin", "circlesMax");

		if (query.Sliders is { } sliders)
			AddInterval(conditions, parameters, "(b.objects->>'Sliders')::int", sliders, "slidersMin", "slidersMax");

		// Created at / Updated at: the query bounds are UTC instants.
		if (query.Created is { } created)
			AddDateInterval(conditions, parameters, "s.created_at", created, "createdMin", "createdMax");

		if (query.Updated is { } updated)
			AddDateInterval(conditions, parameters, "s.updated_at", updated, "updatedMin", "updatedMax");

		// Free text: every whitespace-separated word must match artist, title, creator or version.
		if (!string.IsNullOrWhiteSpace(query.Text))
		{
			var words = query.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			for (var i = 0; i < words.Length; i++)
			{
				var p = $"tw{i}";
				parameters.Add(p, LikePattern.Containing(words[i]));
				conditions.Add(
					$"(s.artist ILIKE @{p} OR s.title ILIKE @{p} OR s.creator ILIKE @{p} OR b.version ILIKE @{p})");
			}
		}

		if (!string.IsNullOrWhiteSpace(query.Creator))
		{
			parameters.Add("creator", LikePattern.Containing(query.Creator));
			conditions.Add("s.creator ILIKE @creator");
		}

		if (!string.IsNullOrWhiteSpace(query.Artist))
		{
			parameters.Add("artist", LikePattern.Containing(query.Artist));
			conditions.Add("s.artist ILIKE @artist");
		}

		if (!string.IsNullOrWhiteSpace(query.Title))
		{
			parameters.Add("title", LikePattern.Containing(query.Title));
			conditions.Add("s.title ILIKE @title");
		}

		if (!string.IsNullOrWhiteSpace(query.Difficulty))
		{
			parameters.Add("version", LikePattern.Containing(query.Difficulty));
			conditions.Add("b.version ILIKE @version");
		}

		// Basil reports every set as Approved; any other status matches nothing.
		if (query.Status is { } status
		    && status != BeatmapStatus.Approved)
			return "1 = 0";

		return conditions.Count == 0 ? string.Empty : string.Join(" AND ", conditions);
	}

	private static void AddInterval<T>(
		List<string> conditions,
		DynamicParameters parameters,
		string column,
		Interval<T> interval,
		string minParam,
		string maxParam)
		where T : struct, IComparable<T>
	{
		if (interval.Min is { } min)
		{
			parameters.Add(minParam, min);
			conditions.Add(interval.MinInclusive ? $"{column} >= @{minParam}" : $"{column} > @{minParam}");
		}

		if (interval.Max is { } max)
		{
			parameters.Add(maxParam, max);
			conditions.Add(interval.MaxInclusive ? $"{column} <= @{maxParam}" : $"{column} < @{maxParam}");
		}
	}

	private static void AddDateInterval(
		List<string> conditions,
		DynamicParameters parameters,
		string column,
		Interval<DateTimeOffset> interval,
		string minParam,
		string maxParam)
	{
		if (interval.Min is { } min)
		{
			parameters.Add(minParam, min.ToUniversalTime());
			conditions.Add(interval.MinInclusive ? $"{column} >= @{minParam}" : $"{column} > @{minParam}");
		}

		if (interval.Max is { } max)
		{
			parameters.Add(maxParam, max.ToUniversalTime());
			conditions.Add(interval.MaxInclusive ? $"{column} <= @{maxParam}" : $"{column} < @{maxParam}");
		}
	}
}