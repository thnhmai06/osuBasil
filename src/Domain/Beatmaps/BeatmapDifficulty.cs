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
///     <see cref="Length" /> serializes as a whole number of seconds on the wire.
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
	public GameMode Mode { get; init; } = Mode;

	/// <summary>The beats per minute of the beatmap.</summary>
	public double Bpm
	{
		get;
		init
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	} = Bpm;

	/// <summary>The total length of the beatmap.</summary>
	public TimeSpan Length
	{
		get;
		init => field = value >= TimeSpan.Zero
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Length cannot be negative.");
	} = Length;

	/// <summary>The circle size setting.</summary>
	public double Cs
	{
		get;
		init => field = double.IsFinite(value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Circle size must be a finite number.");
	} = Cs;

	/// <summary>The approach rate setting.</summary>
	public double Ar
	{
		get;
		init => field = double.IsFinite(value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Approach rate must be a finite number.");
	} = Ar;

	/// <summary>The overall difficulty setting.</summary>
	public double Od
	{
		get;
		init => field = double.IsFinite(value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Overall difficulty must be a finite number.");
	} = Od;

	/// <summary>The health drain rate setting.</summary>
	public double Hp
	{
		get;
		init => field = double.IsFinite(value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Health drain must be a finite number.");
	} = Hp;

	/// <summary>The star rating of the beatmap.</summary>
	public double Star
	{
		get;
		init
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	} = Star;
}