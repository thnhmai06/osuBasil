using Basil.Domain.Game;
using Basil.Shared.Structures.Cryptography;
using Basil.Shared.Utilities;

namespace Basil.Domain.Scores;

/// <summary>A recorded play; two records are equal when they have the same id.</summary>
public sealed class ScoreRecord : IEquatable<ScoreRecord>
{
	/// <summary>The identifier of the score, above zero.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is not above zero.</exception>
	public required int Id
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Score id must be positive.");
	}

	/// <summary>The play itself.</summary>
	public required ScoreData Value { get; init; }

	public bool Equals(ScoreRecord? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	public override bool Equals(object? obj)
	{
		return obj is ScoreRecord other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Id;
	}
}

/// <summary>The facts of one play: who made it, on what, and how it went.</summary>
/// <param name="UserId">The player; null when no player is attached. At least 1 otherwise.</param>
/// <param name="BeatmapHash">The MD5 hash of the beatmap played; null when it is not known.</param>
/// <param name="Mode">The game mode of the play.</param>
/// <param name="Mods">The mods of the play.</param>
/// <param name="LastState">The hit counts, score and combo at the end of the play.</param>
/// <param name="Grade">The letter rating of the play.</param>
/// <param name="IsPassed">True when the player finished the play without failing.</param>
/// <param name="Timestamp">When the play was made.</param>
/// <exception cref="ArgumentOutOfRangeException"><paramref name="UserId"/> is below 1, or <paramref name="Mode"/> or <paramref name="Grade"/> is not a defined value.</exception>
public sealed record ScoreData(
	int? UserId,
	Md5Hash? BeatmapHash,
	GameMode Mode,
	GameMods Mods,
	ScoreState LastState,
	Grade Grade,
	bool IsPassed,
	DateTimeOffset Timestamp)
{
	/// <summary>The letter rating of the play.</summary>
	public readonly Grade Grade = Grade.ThrowIfInvalid();

	/// <summary>The game mode of the play.</summary>
	public readonly GameMode Mode = Mode.ThrowIfInvalid();

	/// <summary>The player; null when no player is attached.</summary>
	public int? UserId = UserId is < 1
		? throw new ArgumentOutOfRangeException(nameof(UserId), UserId, "User ids start at 1.")
		: UserId;

	/// <summary>The checksum that came with the submitted play; null when there is none.</summary>
	public Md5Hash? Checksum { get; init; }

	/// <summary>The performance points the play is worth; null while they are not calculated yet, or when they cannot be.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative or not a finite number.</exception>
	public double? PerformancePoints
	{
		get;
		set => field = value is { } pp && !(double.IsFinite(pp) && pp >= 0)
			? throw new ArgumentOutOfRangeException(nameof(value), value,
				"Performance points must be finite and not negative.")
			: value;
	}

	/// <summary>The accuracy of the play as a percentage from 0 to 100, taken from the final hit counts.</summary>
	public double Accuracy => LastState.HitCounts.CalculateAccuracy(Mode, Mods);
}