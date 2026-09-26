namespace Basil.Domain.Multiplayer.Records;

/// <summary>
///     A match record as read back for report and management purposes.
/// </summary>
public sealed class Match : IEquatable<Match>
{
	/// <summary>
	///     Gets the persistent identifier of the match, used to look it up later — for the match
	///     report, history, or recovery — independent of any live <see cref="Runtime.Room" /> instance.
	/// </summary>
	public required int Id
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Match Id must be positive.");
	}

	/// <summary>Gets or sets the name of the match.</summary>
	public required string Name
	{
		get;
		set => field = string.IsNullOrWhiteSpace(value)
			? throw new ArgumentException("Match name cannot be empty.", nameof(value))
			: value;
	}

	/// <summary>Gets the date and time when the match was created.</summary>
	public required DateTimeOffset CreatedAt { get; init; }

	/// <summary>Gets or sets the date and time when the match ended, if it has ended.</summary>
	public required DateTimeOffset? EndedAt { get; set; }

	/// <summary>
	///     Gets or sets a value that indicates whether the match is publicly visible.
	/// </summary>
	public bool IsVisible { get; set; } = true;

	/// <summary>
	///     Determines whether another match record refers to the same match.
	/// </summary>
	/// <remarks>
	///     Two match records are considered equal when their <see cref="Id" /> values are equal.
	/// </remarks>
	/// <param name="other">The match to compare against, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="other" /> has the same <see cref="Id" />;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool Equals(Match? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	/// <summary>
	///     Determines whether this match record equals another object.
	/// </summary>
	/// <param name="obj">The object to compare against.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="Match" /> with the
	///     same <see cref="Id" />; otherwise, <see langword="false" />.
	/// </returns>
	public override bool Equals(object? obj)
	{
		return obj is Match other && Equals(other);
	}

	/// <summary>
	///     Returns the hash code of this match record.
	/// </summary>
	/// <returns>The <see cref="Id" />, which uniquely identifies the match.</returns>
	public override int GetHashCode()
	{
		return Id;
	}
}