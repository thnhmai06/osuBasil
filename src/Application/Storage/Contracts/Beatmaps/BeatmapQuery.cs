using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Storage.Contracts.Beatmaps;

/// <summary>Which beatmaps a beatmap or beatmapset listing includes.</summary>
/// <param name="Text">
///     Free text matched against the artist, title, creator and difficulty name, or <see langword="null" />
///     for any.
/// </param>
/// <param name="Mode">The game mode a beatmap must be for, or <see langword="null" /> for any.</param>
/// <param name="Hidden">
///     Whether only hidden (<see langword="true" />) or only visible (<see langword="false" />) beatmaps are included,
///     or <see langword="null" /> for both; a beatmap is hidden when it or its beatmapset is. A user's search is
///     narrowed with <see cref="LimitTo" />.
/// </param>
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
	bool? Hidden = null,
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
	BeatmapsetStatus? Status = null,
	Interval<DateTimeOffset>? Created = null,
	Interval<DateTimeOffset>? Updated = null) : IParsable<BeatmapQuery>
{
	/// <summary>Reads a query written in the osu! beatmap search syntax.</summary>
	/// <param name="s">
	///     The search text, for example <c>stars>5 ar=9 mode=0 hidden=0 "some title"</c>; <c>hidden</c> takes <c>1</c>, <c>0</c> or <c>*</c> for both, and <c>mode</c> takes the number
	///     of a game mode.
	/// </param>
	/// <param name="provider">Not used; the syntax does not depend on culture.</param>
	/// <returns>The query; a filter with an unknown key or a value that does not parse stays in <see cref="Text" />.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="s" /> is <see langword="null" />.</exception>
	public static BeatmapQuery Parse(string s, IFormatProvider? provider = null)
	{
		ArgumentNullException.ThrowIfNull(s);

		GameMode? mode = null;
		bool? hidden = null;
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
		BeatmapsetStatus? status = null;
		Interval<DateTimeOffset>? created = null;
		Interval<DateTimeOffset>? updated = null;

		var keywords = SearchSyntax.Parse(s, (key, op, rawValue) =>
		{
			switch (key)
			{
				case "mode" when op is ":" or "=" &&
				                 byte.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture,
					                 out var number) && Enum.IsDefined((GameMode)number):
					mode = (GameMode)number;
					return true;
				case "hidden" when op is ":" or "=" && SearchSyntax.TryParseState(rawValue, out var state):
					hidden = state;
					return true;
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

		return new BeatmapQuery(keywords, mode, hidden, stars, ar, cs, od, hp, bpm, length, circles, sliders,
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
			BeatmapsetStatus? parsed = name switch
			{
				_ when "pending".StartsWith(name, StringComparison.Ordinal) => BeatmapsetStatus.Pending,
				_ when "graveyard".StartsWith(name, StringComparison.Ordinal) => BeatmapsetStatus.Pending,
				_ when "wip".StartsWith(name, StringComparison.Ordinal) => BeatmapsetStatus.Pending,
				_ when "ranked".StartsWith(name, StringComparison.Ordinal) => BeatmapsetStatus.Ranked,
				_ when "approved".StartsWith(name, StringComparison.Ordinal) => BeatmapsetStatus.Approved,
				_ when "qualified".StartsWith(name, StringComparison.Ordinal) => BeatmapsetStatus.Qualified,
				_ when "loved".StartsWith(name, StringComparison.Ordinal) => BeatmapsetStatus.Loved,
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

	/// <summary>Narrows the query to what a user with the given permissions may see.</summary>
	/// <param name="effective">The asker's effective permissions.</param>
	/// <returns>The query, which leaves hidden beatmaps out unless the asker may see them.</returns>
	public BeatmapQuery LimitTo(Permissions effective)
	{
		return effective.Allows(Permissions.TournamentViewHiddenBeatmaps) ? this : this with { Hidden = false };
	}

	/// <summary>Reads a query written in the osu! beatmap search syntax.</summary>
	/// <param name="s">The search text.</param>
	/// <param name="provider">Not used; the syntax does not depend on culture.</param>
	/// <param name="result">The query, or <see langword="null" /> when <paramref name="s" /> is <see langword="null" />.</param>
	/// <returns><see langword="true" /> unless <paramref name="s" /> is <see langword="null" />; any text reads as a query.</returns>
	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider,
		[MaybeNullWhen(false)] out BeatmapQuery result)
	{
		result = s is null ? null : Parse(s, provider);
		return result is not null;
	}
}