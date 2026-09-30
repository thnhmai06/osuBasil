using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Domain.Multiplayer;

/// <summary>
///     A persisted match identified by its id.
/// </summary>
public sealed class Match : IWrapper<MatchData>, IEquatable<Match>
{
	public required int Id
	{
		get;
		init
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>Gets the match data this identity wraps.</summary>
	public required MatchData Value { get; init; }

	/// <summary>
	///     Determines whether another match refers to the same persisted match.
	/// </summary>
	/// <remarks>
	///     Two matches are considered equal when their <see cref="Id" /> values are equal.
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
	///     Determines whether this match equals another object.
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
	///     Returns the hash code of this match.
	/// </summary>
	/// <returns>The <see cref="Id" />, which uniquely identifies the match.</returns>
	public override int GetHashCode()
	{
		return Id.GetHashCode();
	}
}

/// <summary>The data of a match, separate from its persistent identity.</summary>
public sealed class MatchData
{
	/// <summary>Gets or sets the name of the match.</summary>
	public required string Name
	{
		get;
		set => field = string.IsNullOrWhiteSpace(value)
			? throw new ArgumentException("Match name cannot be empty.", nameof(value))
			: value;
	}

	/// <summary>Gets the user who created the match, or <see langword="null" /> for an unattended room.</summary>
	public User? Creator { get; init; }

	/// <summary>Gets the date and time when the match was created.</summary>
	public required DateTimeOffset CreatedAt { get; init; }

	/// <summary>Gets or sets the date and time when the match ended, if it has ended.</summary>
	public required DateTimeOffset? EndedAt { get; set; }

	/// <summary>
	///     Gets or sets a value that indicates whether the match is publicly visible.
	/// </summary>
	public bool IsVisible { get; set; } = true;
}