using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;

namespace Basil.Host.Shared.Views;

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
	public static SetView Of(BeatmapsetRecord set)
	{
		return new SetView(set.Id, set.Metadata.Artist, set.Metadata.Title, set.Metadata.Creator, set.Status,
			set.Visible,
			set.Locked, set.Assets.HasStoryboard, set.CreatedAt, set.UpdatedAt);
	}
}

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
	public static BeatmapView Of(BeatmapRecord map, bool withSet)
	{
		var d = map.Metadata.Difficulty;
		return new BeatmapView(map.Id, map.Metadata.VersionName, d.Mode, d.Star, d.Bpm, d.Ar, d.Cs, d.Od, d.Hp,
			d.Length.TotalSeconds, map.Visible, map.Locked, withSet ? SetView.Of(map.Beatmapset) : null);
	}
}

public sealed record SetDetailView(SetView Set, IReadOnlyList<BeatmapView> Beatmaps);