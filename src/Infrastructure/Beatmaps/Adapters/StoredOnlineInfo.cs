using Basil.Domain.Beatmaps;
using osu.Game.Beatmaps;
using BeatmapDifficulty = Basil.Domain.Beatmaps.BeatmapDifficulty;

namespace Basil.Infrastructure.Beatmaps.Adapters;

/// <summary>What lazer asks of a beatmap's online information, answered from the stored beatmap.</summary>
internal sealed class StoredOnlineInfo(BeatmapRecord beatmap) : IBeatmapOnlineInfo
{
	private readonly BeatmapDifficulty _difficulty = beatmap.Metadata.Difficulty;
	private readonly OsuObjectCount? _osu = beatmap.Metadata.ObjectCount as OsuObjectCount;

	/// <inheritdoc />
	public int? MaxCombo => beatmap.Metadata.ObjectCount.MaxCombo;

	/// <inheritdoc />
	public float ApproachRate => (float)_difficulty.Ar;

	/// <inheritdoc />
	public float CircleSize => (float)_difficulty.Cs;

	/// <inheritdoc />
	public float DrainRate => (float)_difficulty.Hp;

	/// <inheritdoc />
	public float OverallDifficulty => (float)_difficulty.Od;

	/// <inheritdoc />
	public int CircleCount => _osu?.Circles ?? 0;

	/// <inheritdoc />
	public int SliderCount => _osu?.Sliders ?? 0;

	/// <inheritdoc />
	public int SpinnerCount => _osu?.Spinners ?? 0;

	/// <inheritdoc />
	public int PlayCount => 0;

	/// <inheritdoc />
	public int PassCount => 0;

	/// <inheritdoc />
	public APIFailTimes? FailTimes => null;

	/// <inheritdoc />
	public double HitLength => _difficulty.Length.TotalSeconds;
}
