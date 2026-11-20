using Basil.Domain.Game;
using Basil.Shared.Structures.Cryptography;
using Basil.Shared.Utilities;

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
	Md5Hash? BeatmapHash,
	GameMode Mode,
	GameMods Mods,
	ScoreState LastState,
	Grade Grade,
	bool IsPassed,
	DateTimeOffset Timestamp)
{
	public readonly Grade Grade = Grade.ThrowIfInvalid();

	public readonly GameMode Mode = Mode.ThrowIfInvalid();

	public int? UserId = UserId is < 1
		? throw new ArgumentOutOfRangeException(nameof(UserId), UserId, "User ids start at 1.")
		: UserId;

	public Md5Hash? Checksum { get; init; }

	public double? PerformancePoints
	{
		get;
		set => field = value is { } pp && !(double.IsFinite(pp) && pp >= 0)
			? throw new ArgumentOutOfRangeException(nameof(value), value,
				"Performance points must be finite and not negative.")
			: value;
	}

	public double Accuracy => LastState.HitCounts.CalculateAccuracy(Mode, Mods);
}