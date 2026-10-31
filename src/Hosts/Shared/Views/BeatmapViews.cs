using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;

namespace Basil.Host.Shared.Views;

/// <summary>A beatmapset as the API shows it.</summary>
public sealed record SetView(
	int Id,
	string Artist,
	string Title,
	string Creator,
	BeatmapsetStatus Status,
	bool Visible,
	bool Locked,
	bool HasStoryboard,
	DateTimeOffset CreatedAt,
	DateTimeOffset UpdatedAt)
{
	/// <summary>Describes a beatmapset.</summary>
	public static SetView Of(BeatmapsetRecord set)
	{
		return new SetView(set.Id, set.Metadata.Artist, set.Metadata.Title, set.Metadata.Creator, set.Status,
			set.Visible,
			set.Locked, set.Assets.HasStoryboard, set.CreatedAt, set.UpdatedAt);
	}
}

/// <summary>A beatmap as the API shows it; <c>Set</c> is the set it belongs to.</summary>
public sealed record BeatmapView(
	int Id,
	string Version,
	GameMode Mode,
	double Stars,
	double Bpm,
	double Ar,
	double Cs,
	double Od,
	double Hp,
	double LengthSeconds,
	bool Visible,
	bool Locked,
	SetView? Set)
{
	/// <summary>Describes a beatmap, with its set when asked.</summary>
	public static BeatmapView Of(BeatmapRecord map, bool withSet)
	{
		var d = map.Metadata.Difficulty;
		return new BeatmapView(map.Id, map.Metadata.Version, d.Mode, d.Star, d.Bpm, d.Ar, d.Cs, d.Od, d.Hp,
			d.Length.TotalSeconds, map.Visible, map.Locked, withSet ? SetView.Of(map.Beatmapset) : null);
	}
}

/// <summary>A beatmapset with its beatmaps.</summary>
public sealed record SetDetailView(SetView Set, IReadOnlyList<BeatmapView> Beatmaps);