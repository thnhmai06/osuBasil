using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Basil.Domain.Game.Mechanics;
using Basil.Domain.Users;
using Basil.Domain.Game.Beatmaps;
using Basil.Domain.Game.Users;

namespace Basil.Application.Beatmaps.Persistence;

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
	Interval<DateTimeOffset>? Updated = null,
	string? FileName = null) : IParsable<BeatmapQuery>
{
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
		string? fileName = null;

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
				case "filename" or "file":
					fileName = rawValue;
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
			creator, artist, title, difficulty, status, created, updated, fileName);

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

	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider,
		[MaybeNullWhen(false)] out BeatmapQuery result)
	{
		result = s is null ? null : Parse(s, provider);
		return result is not null;
	}

	public BeatmapQuery LimitTo(UserPrivileges effective)
	{
		return MayViewHidden(effective) ? this : this with { Hidden = false };
	}

	public static bool MayViewHidden(UserPrivileges effective)
	{
		return effective.Allows(UserPrivileges.Tournament);
	}
}