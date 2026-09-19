namespace Basil.Domain.Multiplayer.Records;

/// <summary>
///     A round record as read back for report purposes.
/// </summary>
public sealed class Round : IMatchEntry, IEquatable<Round>
{
	/// <summary>The unique identifier of the round.</summary>
	public required int Id { get; init; }

	/// <summary>The match settings the round was played under.</summary>
	public required MatchSettings Settings { get; init; }

	/// <summary>A value that indicates whether the round was aborted.</summary>
	public bool Aborted { get; set; } = false;

	/// <summary>The time the round ended, or <see langword="null" /> while open.</summary>
	public DateTimeOffset? EndedAt { get; set; }

	/// <summary>Gets a value that indicates whether this round equals another by id.</summary>
	/// <param name="other">The round to compare, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="other" /> is non-null and has the same
	///     <see cref="Id" /> as this round; otherwise, <see langword="false" />.
	/// </returns>
	public bool Equals(Round? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	/// <summary>The match the round belongs to.</summary>
	public required Match Match { get; init; }

	/// <summary>The time the round started.</summary>
	public required DateTimeOffset OccurredAt { get; init; }

	/// <summary>Gets a value that indicates whether this round equals another object by id.</summary>
	/// <param name="obj">The object to compare, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="Round" /> that equals
	///     this one; otherwise, <see langword="false" />.
	/// </returns>
	public override bool Equals(object? obj)
	{
		return obj is Round other && Equals(other);
	}

	/// <summary>Returns a hash code equal to the round's <see cref="Id" />.</summary>
	/// <returns>A hash code consistent with the round's value equality, which compares <see cref="Id" />.</returns>
	public override int GetHashCode()
	{
		return Id;
	}
}