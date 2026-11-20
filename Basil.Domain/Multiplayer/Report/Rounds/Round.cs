using Basil.Domain.Multiplayer.Report.Matches;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Multiplayer.Report.Rounds;

/// <summary>One play of a beatmap within a match; two rounds are equal when they share a match and an index.</summary>
public sealed class Round : IMatchEntry, IEquatable<Round>
{
	/// <summary>Gets the 1-based position of the round in its match.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The index is below 1.</exception>
	public required int Index
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Round number must be positive.");
	}

	/// <summary>Gets the settings the round was played with.</summary>
	public required MatchSettings Settings { get; init; }

	/// <summary>Gets the hash of the beatmap the round was played on.</summary>
	public required Md5Hash BeatmapHash { get; init; }

	/// <summary>Gets or sets whether the round ended early instead of being played out.</summary>
	public bool Aborted { get; set; } = false;

	/// <summary>Gets or sets the result of the round, or <see langword="null"/> before it is decided or when no score was received.</summary>
	/// <exception cref="ArgumentException">The result belongs to another round.</exception>
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

	/// <summary>Gets when the round started.</summary>
	public required DateTimeOffset StartedAt { get; init; }

	/// <summary>Gets or sets when the round ended, or <see langword="null"/> while it is running.</summary>
	public required DateTimeOffset? EndedAt { get; set; }

	/// <summary>Gets the match the round belongs to.</summary>
	public required MatchRecord Match { get; init; }

	DateTimeOffset IMatchEntry.Timestamp => StartedAt;

	public bool Equals(Round? other)
	{
		if (other is null) return false;
		return Match.Equals(other.Match) && Index == other.Index;
	}

	public override bool Equals(object? obj)
	{
		return obj is Round other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Match, Index);
	}
}