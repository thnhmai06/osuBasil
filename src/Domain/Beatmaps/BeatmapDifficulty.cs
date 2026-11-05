using Basil.Domain.Common;
using Basil.Domain.Mechanics;

namespace Basil.Domain.Beatmaps;

/// <summary>
///     Describes the gameplay-mechanical facts about a beatmap.
/// </summary>
/// <param name="Mode">The game mode the beatmap is played in.</param>
/// <param name="Bpm">The beats per minute of the beatmap.</param>
/// <param name="Length">The total length of the beatmap.</param>
/// <param name="Cs">The circle size setting.</param>
/// <param name="Ar">The approach rate setting.</param>
/// <param name="Od">The overall difficulty setting.</param>
/// <param name="Hp">The health drain rate setting.</param>
/// <param name="Star">The star rating of the beatmap.</param>
/// <remarks>
///     Under mods the settings may leave the range a beatmap file allows (approach rate above 10 under DoubleTime,
///     below 0 under HalfTime), so they are only required to be finite.
/// </remarks>
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
	/// <exception cref="ArgumentOutOfRangeException">The value is not a defined game mode.</exception>
	public GameMode Mode
	{
		get;
		init => field = value.ThrowIfUndefined();
	} = Mode.ThrowIfUndefined();

	/// <summary>The beats per minute of the beatmap.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative or not a finite number.</exception>
	public double Bpm
	{
		get;
		init => field = CheckPositive(value);
	} = CheckPositive(Bpm);

	/// <summary>The total length of the beatmap.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The length is negative.</exception>
	public TimeSpan Length
	{
		get;
		init => field = CheckLength(value);
	} = CheckLength(Length);

	/// <summary>The circle size setting.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is not a finite number.</exception>
	public double Cs
	{
		get;
		init => field = CheckFinite(value);
	} = CheckFinite(Cs);

	/// <summary>The approach rate setting.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is not a finite number.</exception>
	public double Ar
	{
		get;
		init => field = CheckFinite(value);
	} = CheckFinite(Ar);

	/// <summary>The overall difficulty setting.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is not a finite number.</exception>
	public double Od
	{
		get;
		init => field = CheckFinite(value);
	} = CheckFinite(Od);

	/// <summary>The health drain rate setting.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is not a finite number.</exception>
	public double Hp
	{
		get;
		init => field = CheckFinite(value);
	} = CheckFinite(Hp);

	/// <summary>The star rating of the beatmap.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative or not a finite number.</exception>
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