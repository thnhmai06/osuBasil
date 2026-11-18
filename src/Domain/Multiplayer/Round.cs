using Basil.Domain.Cryptography;

namespace Basil.Domain.Multiplayer;

public sealed class Round : IMatchEntry, IEquatable<Round>
{
	public required int Index
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Round number must be positive.");
	}

	public required Md5 BeatmapHash { get; init; }

	public required MatchSettings Settings { get; init; }

	public bool Aborted { get; set; } = false;

	public RoundResult? Result
	{
		get;
		set
		{
			if (value is not null && !value.Round.Equals(this))
				throw new ArgumentException("The result must belong to this round.", nameof(value));

			field = value;
		}
	} = null;

	public required DateTimeOffset StartedAt { get; init; }

	public required DateTimeOffset? EndedAt { get; set; }

	public bool Equals(Round? other)
	{
		if (other is null) return false;
		return Match.Equals(other.Match) && Index == other.Index;
	}

	public required MatchRecord Match { get; init; }

	DateTimeOffset IMatchEntry.Timestamp => StartedAt;

	public override bool Equals(object? obj)
	{
		return obj is Round other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Match, Index);
	}
}