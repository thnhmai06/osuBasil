using System.Globalization;
using Basil.Application.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;

namespace Basil.Application.Beatmaps;

/// <summary>Which beatmaps a beatmap or beatmapset listing includes.</summary>
/// <param name="Text">Free text matched against the artist, title, creator and difficulty name, or <see langword="null" /> for any.</param>
/// <param name="Mode">The game mode a beatmap must be for, or <see langword="null" /> for any.</param>
/// <param name="IncludeHidden">Whether hidden beatmaps and beatmapsets are included.</param>
/// <param name="Stars">The star rating range, or <see langword="null" /> for any.</param>
/// <param name="Ar">The approach rate range, or <see langword="null" /> for any.</param>
/// <param name="Cs">The circle size range (the key count in osu!mania), or <see langword="null" /> for any.</param>
/// <param name="Od">The overall difficulty range, or <see langword="null" /> for any.</param>
/// <param name="Hp">The health drain range, or <see langword="null" /> for any.</param>
/// <param name="Bpm">The beats-per-minute range, or <see langword="null" /> for any.</param>
/// <param name="Length">The length range in seconds, or <see langword="null" /> for any.</param>
/// <param name="Circles">The circle count range, or <see langword="null" /> for any.</param>
/// <param name="Sliders">The slider count range, or <see langword="null" /> for any.</param>
/// <param name="Creator">Text the beatmapset's creator name must contain, or <see langword="null" /> for any.</param>
/// <param name="Artist">Text the artist must contain, or <see langword="null" /> for any.</param>
/// <param name="Title">Text the title must contain, or <see langword="null" /> for any.</param>
/// <param name="Difficulty">Text the difficulty name must contain, or <see langword="null" /> for any.</param>
/// <param name="Status">The ranked status a beatmapset must report, or <see langword="null" /> for any.</param>
/// <param name="Created">When the beatmapset must have been created, or <see langword="null" /> for any time.</param>
/// <param name="Updated">When the beatmapset must last have been updated, or <see langword="null" /> for any time.</param>
public sealed record BeatmapQuery(
	string? Text = null,
	GameMode? Mode = null,
	bool IncludeHidden = false,
	Interval<double>? Stars = null,
	Interval<double>? Ar = null,
	Interval<double>? Cs = null,
	Interval<double>? Od = null,
	Interval<double>? Hp = null,
	Interval<double>? Bpm = null,
	Interval<double>? Length = null,
	Interval<int>? Circles = null,
	Interval<int>? Sliders = null,
	string? Creator = null,
	string? Artist = null,
	string? Title = null,
	string? Difficulty = null,
	BeatmapStatus? Status = null,
	Interval<DateTimeOffset>? Created = null,
	Interval<DateTimeOffset>? Updated = null)
{
	/// <summary>Reads a query written in the osu! beatmap search syntax.</summary>
	/// <param name="text">The search text, for example <c>stars>5 ar=9 "some title"</c>.</param>
	/// <param name="mode">The game mode a beatmap must be for, or <see langword="null" /> for any.</param>
	/// <param name="includeHidden">Whether hidden beatmaps and beatmapsets are included.</param>
	/// <returns>The query; a filter with an unknown key or a value that does not parse stays in <see cref="Text" />.</returns>
	public static BeatmapQuery Parse(string text, GameMode? mode = null, bool includeHidden = false)
	{
		Interval<double>? stars = null;
		Interval<double>? ar = null;
		Interval<double>? cs = null;
		Interval<double>? od = null;
		Interval<double>? hp = null;
		Interval<double>? bpm = null;
		Interval<double>? length = null;
		Interval<int>? circles = null;
		Interval<int>? sliders = null;
		string? creator = null;
		string? artist = null;
		string? title = null;
		string? difficulty = null;
		BeatmapStatus? status = null;
		Interval<DateTimeOffset>? created = null;
		Interval<DateTimeOffset>? updated = null;

		var keywords = SearchSyntax.Parse(text, (key, op, rawValue) =>
		{
			switch (key)
			{
				case "stars" or "star": return TrySetDouble(rawValue, op, stars, v => stars = v);
				case "ar": return TrySetDouble(rawValue, op, ar, v => ar = v);
				case "cs" or "keys" or "key": return TrySetDouble(rawValue, op, cs, v => cs = v);
				case "od": return TrySetDouble(rawValue, op, od, v => od = v);
				case "hp" or "dr": return TrySetDouble(rawValue, op, hp, v => hp = v);
				case "bpm": return TrySetDouble(rawValue, op, bpm, v => bpm = v);
				case "length": return TrySetLength(rawValue, op);
				case "circles": return TrySetInt(rawValue, op, circles, v => circles = v);
				case "sliders": return TrySetInt(rawValue, op, sliders, v => sliders = v);
				case "creator":
					creator = rawValue;
					return true;
				case "artist":
					artist = rawValue;
					return true;
				case "title":
					title = rawValue;
					return true;
				case "difficulty":
					difficulty = rawValue;
					return true;
				case "status": return TrySetStatus(rawValue);
				case "created" or "submitted": return TrySetDate(rawValue, op, created, v => created = v);
				case "updated": return TrySetDate(rawValue, op, updated, v => updated = v);
				default: return false;
			}
		});

		return new BeatmapQuery(keywords, mode, includeHidden, stars, ar, cs, od, hp, bpm, length, circles, sliders,
			creator, artist, title, difficulty, status, created, updated);

		static bool TrySetDouble(string raw, string op, Interval<double>? current, Action<Interval<double>> set)
		{
			if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return false;
			set(SearchSyntax.Narrow(current, op, value));
			return true;
		}

		static bool TrySetInt(string raw, string op, Interval<int>? current, Action<Interval<int>> set)
		{
			if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return false;
			set(SearchSyntax.Narrow(current, op, value));
			return true;
		}

		bool TrySetLength(string raw, string op)
		{
			if (!SearchSyntax.TryParseSeconds(raw, out var seconds)) return false;
			length = SearchSyntax.Narrow(length, op, seconds);
			return true;
		}

		bool TrySetStatus(string raw)
		{
			var name = raw.ToLowerInvariant();
			BeatmapStatus? parsed = name switch
			{
				_ when "pending".StartsWith(name, StringComparison.Ordinal) => BeatmapStatus.Pending,
				_ when "graveyard".StartsWith(name, StringComparison.Ordinal) => BeatmapStatus.Pending,
				_ when "wip".StartsWith(name, StringComparison.Ordinal) => BeatmapStatus.Pending,
				_ when "ranked".StartsWith(name, StringComparison.Ordinal) => BeatmapStatus.Ranked,
				_ when "approved".StartsWith(name, StringComparison.Ordinal) => BeatmapStatus.Approved,
				_ when "qualified".StartsWith(name, StringComparison.Ordinal) => BeatmapStatus.Qualified,
				_ when "loved".StartsWith(name, StringComparison.Ordinal) => BeatmapStatus.Loved,
				_ => null
			};
			if (parsed is null) return false;
			status = parsed;
			return true;
		}

		static bool TrySetDate(string raw, string op, Interval<DateTimeOffset>? current,
			Action<Interval<DateTimeOffset>> set)
		{
			if (SearchSyntax.NarrowDate(current, op, raw) is not { } narrowed) return false;
			set(narrowed);
			return true;
		}
	}
}