using Basil.Domain.Beatmaps;
using osu.Game.Beatmaps;
using BeatmapDifficulty = Basil.Domain.Beatmaps.BeatmapDifficulty;

namespace Basil.Infrastructure.Beatmaps.Adapters;

internal sealed class StoredOnlineInfo(BeatmapRecord beatmap) : IBeatmapOnlineInfo
{
	private readonly BeatmapDifficulty _difficulty = beatmap.Metadata.Difficulty;
	private readonly OsuObjectCount? _osu = beatmap.Metadata.ObjectCount as OsuObjectCount;

	public int? MaxCombo => beatmap.Metadata.ObjectCount.MaxCombo;

	public float ApproachRate => (float)_difficulty.Ar;

	public float CircleSize => (float)_difficulty.Cs;

	public float DrainRate => (float)_difficulty.Hp;

	public float OverallDifficulty => (float)_difficulty.Od;

	public int CircleCount => _osu?.Circles ?? 0;

	public int SliderCount => _osu?.Sliders ?? 0;

	public int SpinnerCount => _osu?.Spinners ?? 0;

	public int PlayCount => 0;

	public int PassCount => 0;

	public APIFailTimes? FailTimes => null;

	public double HitLength => _difficulty.Length.TotalSeconds;
}
