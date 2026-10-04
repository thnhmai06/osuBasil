using System.Globalization;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Utilities;

namespace Basil.Domain.Scores;

/// <summary>A stored score identified by its id.</summary>
public sealed class Score : IWrapper<ScoreData>, IEquatable<Score>
{
	/// <summary>Gets the unique identifier of the score.</summary>
	public required int Id
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Score id must be positive.");
	}

	/// <summary>Determines whether another score refers to the same stored score.</summary>
	/// <param name="other">The score to compare against, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="other" /> has the same <see cref="Id" />;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool Equals(Score? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	/// <summary>Gets the score data this identity wraps.</summary>
	public required ScoreData Value { get; init; }

	/// <summary>Determines whether this score equals another object.</summary>
	/// <param name="obj">The object to compare against.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="Score" /> with the
	///     same <see cref="Id" />; otherwise, <see langword="false" />.
	/// </returns>
	public override bool Equals(object? obj)
	{
		return obj is Score other && Equals(other);
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
	Grade Grade,
	bool IsPassed,
	bool IsFullCombo,
	DateTimeOffset Timestamp)
{
	public int TotalScore { get; init; } = TotalScore >= 0
		? TotalScore
		: throw new ArgumentOutOfRangeException(nameof(TotalScore), "TotalScore must be non-negative.");

	public short MaxCombo { get; init; } = MaxCombo >= 0
		? MaxCombo
		: throw new ArgumentOutOfRangeException(nameof(MaxCombo), "MaxCombo must be non-negative.");

	/// <summary>Gets the multiplayer round the play was made in, or <see langword="null" /> outside a round.</summary>
	public Round? Round { get; init; }

	/// <summary>
	///     Gets the checksum the client computed for the submission of this score, which identifies the submission,
	///     or <see langword="null" /> when it is not known.
	/// </summary>
	public Md5? Checksum { get; init; }

	/// <summary>
	///     Gets the team the player was on in the round, or <see langword="null" /> outside a team round.
	/// </summary>
	public GameTeam? Team { get; init; }

	/// <summary>
	///     Gets the play's accuracy, computed from its hit counts under its mode and mods.
	/// </summary>
	public double Accuracy => HitCounts.CalculateAccuracy(Mode, Mods);

	/// <summary>
	///     Parses the scoring fields of a submission into a <see cref="ScoreData" />.
	/// </summary>
	/// <param name="submitFields">
	///     The colon-delimited submission fields that follow the submission MD5 entry. Indexes 1
	///     through 14 carry the hit counts, total score, max combo, full-combo flag, grade, mods,
	///     passed flag, mode, and occurrence time.
	/// </param>
	/// <param name="beatmapHash">The beatmap MD5 to carry into the parsed score, if known.</param>
	/// <param name="userId">The user ID to carry into the parsed score, if known.</param>
	/// <returns>The parsed score.</returns>
	public static ScoreData Parse(IReadOnlyList<string> submitFields, Md5? beatmapHash = null, int? userId = null)
	{
		var hitCounts = new HitCounts(
			int.Parse(submitFields[1], CultureInfo.InvariantCulture),
			int.Parse(submitFields[2], CultureInfo.InvariantCulture),
			int.Parse(submitFields[3], CultureInfo.InvariantCulture),
			int.Parse(submitFields[4], CultureInfo.InvariantCulture),
			int.Parse(submitFields[5], CultureInfo.InvariantCulture),
			int.Parse(submitFields[6], CultureInfo.InvariantCulture));

		return new ScoreData
		(
			userId,
			beatmapHash,
			(GameMode)int.Parse(submitFields[13], CultureInfo.InvariantCulture),
			(GameMods)int.Parse(submitFields[11], CultureInfo.InvariantCulture),
			hitCounts,
			int.Parse(submitFields[7], CultureInfo.InvariantCulture),
			short.Parse(submitFields[8], CultureInfo.InvariantCulture), Enum.Parse<Grade>(submitFields[10], true),
			submitFields[12] == "True",
			submitFields[9] == "True",
			DateTime.ParseExact(submitFields[14], "yyMMddHHmmss", CultureInfo.InvariantCulture)
		);
	}
}