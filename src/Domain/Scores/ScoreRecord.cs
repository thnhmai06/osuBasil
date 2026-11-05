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
/// <param name="LastState">
///     The score at the end of the play: hit counts, total score, combos and whether it was a full
///     combo.
/// </param>
/// <param name="Grade">The grade earned.</param>
/// <param name="IsPassed">Whether the play was passed.</param>
/// <param name="Timestamp">The date and time when the play occurred.</param>
public sealed record ScoreData(
	int? UserId,
	Md5? BeatmapHash,
	GameMode Mode,
	GameMods Mods,
	ScoreState LastState,
	Grade Grade,
	bool IsPassed,
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

	/// <summary>Gets the score at the end of the play: hit counts, total score and combos.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The total score is negative.</exception>
	public ScoreState LastState
	{
		get;
		init => field = Check(value);
	} = Check(LastState);

	/// <summary>
	///     Gets the checksum the client computed for the submission of this score, which identifies the submission,
	///     or <see langword="null" /> when it is not known.
	/// </summary>
	public Md5? Checksum { get; init; }

	/// <summary>
	///     Gets the play's accuracy, computed from its hit counts under its mode and mods.
	/// </summary>
	public double Accuracy => LastState.HitCounts.CalculateAccuracy(Mode, Mods);

	private static ScoreState Check(ScoreState value)
	{
		return value.TotalScore >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value.TotalScore,
				"The total score must not be negative.");
	}

	private static int? CheckUserId(int? value)
	{
		return value is < 1
			? throw new ArgumentOutOfRangeException(nameof(value), value, "User ids start at 1.")
			: value;
	}
}

/// <summary>The state of a score at one moment of a play: what has been hit, scored and chained so far.</summary>
/// <param name="HitCounts">The judgment counts so far.</param>
/// <param name="TotalScore">The total score so far.</param>
/// <param name="MaxCombo">The maximum combo reached so far, as the replay format stores it (unsigned 16-bit).</param>
/// <param name="CurrentCombo">The combo running at this moment, as the replay format stores it (unsigned 16-bit).</param>
/// <param name="IsFullCombo">Whether no combo has been broken so far.</param>
public readonly record struct ScoreState(
	HitCounts HitCounts,
	int TotalScore,
	ushort MaxCombo,
	ushort CurrentCombo,
	bool IsFullCombo);