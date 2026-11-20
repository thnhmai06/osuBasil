using osu.Game.Beatmaps;

namespace Basil.Infrastructure.Rulesets.Adapters;

/// <summary>What the calculation of a play needs to know about a beatmap, kept beside its attributes.</summary>
internal sealed record BeatmapSnapshot(
	int MaxCombo,
	double Ar,
	double Cs,
	double Hp,
	double Od,
	int Circles,
	int Sliders,
	int Spinners,
	double HitLength);

internal sealed class StoredOnlineInfo(BeatmapSnapshot snapshot) : IBeatmapOnlineInfo
{
	public int? MaxCombo => snapshot.MaxCombo;

	public float ApproachRate => (float)snapshot.Ar;

	public float CircleSize => (float)snapshot.Cs;

	public float DrainRate => (float)snapshot.Hp;

	public float OverallDifficulty => (float)snapshot.Od;

	public int CircleCount => snapshot.Circles;

	public int SliderCount => snapshot.Sliders;

	public int SpinnerCount => snapshot.Spinners;

	public int PlayCount => 0;

	public int PassCount => 0;

	public APIFailTimes? FailTimes => null;

	public double HitLength => snapshot.HitLength;
}