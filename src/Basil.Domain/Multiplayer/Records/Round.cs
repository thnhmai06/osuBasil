namespace Basil.Domain.Multiplayer.Records;

/// <summary>
///     A round record as read back for report purposes.
/// </summary>
public sealed class Round : IMatchEntry, IEquatable<Round>
{
	/// <summary>The unique identifier of the round.</summary>
	public required int Id { get; init; }

	public required Match Match { get; init; }

	public required MatchSettings Settings { get; init; }

	/// <summary>A value that indicates whether the round was aborted.</summary>
	public bool Aborted { get; set; } = false;

	/// <summary>The time the round started.</summary>
	public required DateTimeOffset OccurredAt { get; init; }

	/// <summary>The time the round ended, or <see langword="null" /> while open.</summary>
	public DateTimeOffset? EndedAt { get; set; }

	public bool Equals(Round? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	public override bool Equals(object? obj)
	{
		return obj is Round other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Id;
	}
}