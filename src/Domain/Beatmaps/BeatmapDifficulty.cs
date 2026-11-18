using Basil.Domain.Mechanics;
using Basil.Domain.Shared;

namespace Basil.Domain.Beatmaps;

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
	public GameMode Mode
	{
		get;
		init => field = value.ThrowIfUndefined();
	} = Mode.ThrowIfUndefined();

	public double Bpm
	{
		get;
		init => field = CheckPositive(value);
	} = CheckPositive(Bpm);

	public TimeSpan Length
	{
		get;
		init => field = CheckLength(value);
	} = CheckLength(Length);

	public double Cs
	{
		get;
		init => field = CheckFinite(value);
	} = CheckFinite(Cs);

	public double Ar
	{
		get;
		init => field = CheckFinite(value);
	} = CheckFinite(Ar);

	public double Od
	{
		get;
		init => field = CheckFinite(value);
	} = CheckFinite(Od);

	public double Hp
	{
		get;
		init => field = CheckFinite(value);
	} = CheckFinite(Hp);

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