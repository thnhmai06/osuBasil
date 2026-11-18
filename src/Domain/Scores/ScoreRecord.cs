using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Shared;

namespace Basil.Domain.Scores;

public sealed class ScoreRecord : IEquatable<ScoreRecord>
{
	public required int Id
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Score id must be positive.");
	}

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
	public int? UserId
	{
		get;
		init => field = CheckUserId(value);
	} = CheckUserId(UserId);

	public GameMode Mode
	{
		get;
		init => field = value.ThrowIfUndefined();
	} = Mode.ThrowIfUndefined();

	public Grade Grade
	{
		get;
		init => field = value.ThrowIfUndefined();
	} = Grade.ThrowIfUndefined();

	public ScoreState LastState
	{
		get;
		init => field = Check(value);
	} = Check(LastState);

	public Md5? Checksum { get; init; }

	public double? Pp
	{
		get;
		init => field = value is { } pp && !(double.IsFinite(pp) && pp >= 0)
			? throw new ArgumentOutOfRangeException(nameof(value), value, "Performance points must be finite and not negative.")
			: value;
	}

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

public readonly record struct ScoreState(
	HitCounts HitCounts,
	int TotalScore,
	ushort MaxCombo,
	ushort CurrentCombo,
	bool IsFullCombo);