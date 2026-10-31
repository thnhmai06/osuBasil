using Basil.Domain.Common;
using Basil.Domain.Mechanics;

namespace Basil.Domain.Scores;

/// <summary>A stored score identified by its id.</summary>
public sealed class ScoreRecord : IEquatable<ScoreRecord>
{
	/// <summary>Gets the unique identifier of the score.</summary>
	public required int Id
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Score id must be positive.");
	}

	/// <summary>Gets the score data this identity wraps.</summary>
	public required ScoreData Value { get; init; }

	/// <summary>Determines whether another score refers to the same stored score.</summary>
	/// <param name="other">The score to compare against, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="other" /> has the same <see cref="Id" />;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool Equals(ScoreRecord? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	/// <summary>Determines whether this score equals another object.</summary>
	/// <param name="obj">The object to compare against.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="ScoreRecord" /> with the
	///     same <see cref="Id" />; otherwise, <see langword="false" />.
	/// </returns>
	public override bool Equals(object? obj)
	{
		return obj is ScoreRecord other && Equals(other);
	}

	/// <summary>Returns the hash code of this score.</summary>
	/// <returns>The <see cref="Id" />, which uniquely identifies the score.</returns>
	public override int GetHashCode()
	{
		return Id;
	}
}

/// <summary>Represents the scoring fields of a play as submitted by the osu! client, without a stored identity.</summary>
/// <param name="UserId">The user ID of the player, when known.</param>
/// <param name="BeatmapHash">The MD5 checksum of the beatmap played, when known.</param>
/// <param name="Mode">The game mode the play used.</param>
/// <param name="Mods">The mods applied to the play.</param>
/// <param name="HitCounts">The judgment counts of the play.</param>
/// <param name="TotalScore">The total score achieved.</param>
/// <param name="MaxCombo">The maximum combo reached.</param>
/// <param name="CurrentCombo">The combo count at the end of the beatmap.</param>
/// <param name="Grade">The grade earned.</param>
/// <param name="IsPassed">Whether the play was passed.</param>
/// <param name="IsFullCombo">Whether the play was a full combo.</param>
/// <param name="Timestamp">The date and time when the play occurred.</param>
public sealed record ScoreData(
	int? UserId,
	Md5? BeatmapHash,
	GameMode Mode,
	GameMods Mods,
	HitCounts HitCounts,
	int TotalScore,
	short MaxCombo,
	short CurrentCombo,
	Grade Grade,
	bool IsPassed,
	bool IsFullCombo,
	DateTimeOffset Timestamp)
{
	/// <summary>Gets the user ID of the player, when known.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The user id is not positive.</exception>
	public int? UserId
	{
		get;
		init => field = CheckUserId(value);
	} = CheckUserId(UserId);

	/// <summary>Gets the game mode the play used.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The game mode is not a defined value.</exception>
	public GameMode Mode
	{
		get;
		init => field = value.ThrowIfUndefined();
	} = Mode.ThrowIfUndefined();

	/// <summary>Gets the grade earned.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The grade is not a defined value.</exception>
	public Grade Grade
	{
		get;
		init => field = value.ThrowIfUndefined();
	} = Grade.ThrowIfUndefined();

	/// <summary>Gets the total score achieved.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The total score is negative.</exception>
	public int TotalScore
	{
		get;
		init => field = Check(value);
	} = Check(TotalScore);

	/// <summary>Gets the maximum combo reached.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The maximum combo is negative.</exception>
	public short MaxCombo
	{
		get;
		init => field = Check(value);
	} = Check(MaxCombo);

	/// <summary>Gets the combo count at the end of the beatmap.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The combo count is negative.</exception>
	public short CurrentCombo
	{
		get;
		init => field = Check(value);
	} = Check(CurrentCombo);

	/// <summary>
	///     Gets the checksum the client computed for the submission of this score, which identifies the submission,
	///     or <see langword="null" /> when it is not known.
	/// </summary>
	public Md5? Checksum { get; init; }

	/// <summary>
	///     Gets the play's accuracy, computed from its hit counts under its mode and mods.
	/// </summary>
	public double Accuracy => HitCounts.CalculateAccuracy(Mode, Mods);

	private static int Check(int value)
	{
		return value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "The value must not be negative.");
	}

	private static short Check(short value)
	{
		return value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "The value must not be negative.");
	}

	private static int? CheckUserId(int? value)
	{
		return value is { } id && id < 1
			? throw new ArgumentOutOfRangeException(nameof(value), value, "User ids start at 1.")
			: value;
	}
}