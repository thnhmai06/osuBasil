using Basil.Domain.Game;
using Basil.Shared.Utilities;

namespace Basil.Domain.Beatmaps;

/// <summary>The difficulty values of one beatmap in one game mode.</summary>
/// <param name="Mode">The game mode the beatmap is played in.</param>
/// <param name="Bpm">The tempo in beats per minute; not negative.</param>
/// <param name="Length">The playable length of the beatmap; not negative.</param>
/// <param name="Cs">The circle size (key count in mania); finite.</param>
/// <param name="Ar">The approach rate; finite.</param>
/// <param name="Od">The overall difficulty, which sets the hit windows; finite.</param>
/// <param name="Hp">The health drain rate; finite.</param>
/// <param name="Star">The star rating; not negative.</param>
/// <exception cref="ArgumentOutOfRangeException">A value is outside the range given for its parameter, or <paramref name="Mode"/> is not a defined mode.</exception>
public readonly record struct BeatmapDifficulty(
	GameMode Mode,
	double Bpm,
	TimeSpan Length,
	double Cs,
	double Ar,
	double Od,
	double Hp,
	double Star)
{
	/// <summary>The game mode the beatmap is played in.</summary>
	public GameMode Mode
	{
		get;
		init => field = value.ThrowIfInvalid();
	} = Mode.ThrowIfInvalid();

	/// <summary>The tempo in beats per minute.</summary>
	public double Bpm
	{
		get;
		init => field = CheckPositive(value);
	} = CheckPositive(Bpm);

	/// <summary>The playable length of the beatmap.</summary>
	public TimeSpan Length
	{
		get;
		init => field = CheckLength(value);
	} = CheckLength(Length);

	/// <summary>The circle size (key count in mania).</summary>
	public double Cs
	{
		get;
		init => field = CheckFinite(value);
	} = CheckFinite(Cs);

	/// <summary>The approach rate.</summary>
	public double Ar
	{
		get;
		init => field = CheckFinite(value);
	} = CheckFinite(Ar);

	/// <summary>The overall difficulty, which sets the hit windows.</summary>
	public double Od
	{
		get;
		init => field = CheckFinite(value);
	} = CheckFinite(Od);

	/// <summary>The health drain rate.</summary>
	public double Hp
	{
		get;
		init => field = CheckFinite(value);
	} = CheckFinite(Hp);

	/// <summary>The star rating.</summary>
	public double Star
	{
		get;
		init => field = CheckPositive(value);
	} = CheckPositive(Star);

	private static double CheckPositive(double value)
	{
		return double.IsFinite(value) && value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"The value must be a finite number of at least zero.");
	}

	private static double CheckFinite(double value)
	{
		return double.IsFinite(value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be a finite number.");
	}

	private static TimeSpan CheckLength(TimeSpan value)
	{
		return value >= TimeSpan.Zero
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "Length cannot be negative.");
	}
}