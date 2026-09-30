using Basil.Domain.Utilities;

namespace Basil.Domain.Multiplayer;

/// <summary>
///     A round record as read back for report purposes.
/// </summary>
public sealed class Round : IMatchRecord, IEquatable<Round>
{
	/// <summary>Gets the position of the round within its match, starting at 1.</summary>
	/// <remarks>An aborted round keeps its number; the next round takes the following one.</remarks>
	public required int Number
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Round number must be positive.");
	}

	/// <summary>The match the round belongs to.</summary>
	public required Match Match { get; init; }

	/// <summary>Gets or sets the currently selected beatmap.</summary>
	/// <remarks>
	///     A <see langword="null" /> value means that no beatmap has been selected yet — not that a
	///     selected beatmap could not be found.
	/// </remarks>
	public required Md5 BeatmapHash { get; init; }

	/// <summary>The match settings the round was played under.</summary>
	public required MatchSettings Settings { get; init; }

	/// <summary>The time the round started.</summary>
	public required DateTimeOffset StartedAt { get; init; }

	/// <inheritdoc />
	DateTimeOffset IMatchRecord.Timestamp => StartedAt;

	/// <summary>The time the round ended, or <see langword="null" /> while open.</summary>
	public required DateTimeOffset? EndedAt { get; set; }

	/// <summary>A value that indicates whether the round was aborted.</summary>
	public bool Aborted { get; set; } = false;

	/// <summary>Determines whether another round is the same round of the same match.</summary>
	/// <param name="other">The round to compare, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="other" /> belongs to the same <see cref="Match" />
	///     and has the same <see cref="Number" />; otherwise, <see langword="false" />.
	/// </returns>
	public bool Equals(Round? other)
	{
		if (other is null) return false;
		return Match.Equals(other.Match) && Number == other.Number;
	}

	/// <summary>Determines whether this round equals another object.</summary>
	/// <param name="obj">The object to compare, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="Round" /> that equals
	///     this one; otherwise, <see langword="false" />.
	/// </returns>
	public override bool Equals(object? obj)
	{
		return obj is Round other && Equals(other);
	}

	/// <summary>Returns a hash code consistent with the round's equality.</summary>
	/// <returns>A hash code combining the round's match and number.</returns>
	public override int GetHashCode()
	{
		return HashCode.Combine(Match, Number);
	}
}