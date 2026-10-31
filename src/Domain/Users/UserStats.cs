using Basil.Domain.Common;
using Basil.Domain.Mechanics;

namespace Basil.Domain.Users;

/// <summary>
///     Represents a user's cumulative score statistics for one game mode, as reported to clients.
/// </summary>
/// <remarks>
///     Basil does not calculate accuracy, performance points, or leaderboard rank the way osu! does;
///     <see cref="Accuracy" />, <see cref="Pp" />, and <see cref="Rank" /> are fixed values clients
///     display in place of a real calculation.
/// </remarks>
public sealed class UserStats : IEquatable<UserStats>
{
	/// <summary>Gets the fixed accuracy value Basil reports to clients, in place of a real calculation.</summary>
	public static double Accuracy => 100.0;

	/// <summary>Gets the fixed performance-points value Basil reports to clients, in place of a real calculation.</summary>
	public static int Pp => 727;

	/// <summary>Gets the fixed leaderboard rank Basil reports to clients, in place of a real calculation.</summary>
	public int Rank => UserId;

	/// <summary>The identifier of the user these statistics belong to.</summary>
	public required int UserId { get; init; }

	/// <summary>The game mode these statistics are for.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is not a defined game mode.</exception>
	public required GameMode Mode
	{
		get;
		init => field = value.ThrowIfUndefined();
	}

	/// <summary>The cumulative score across every submitted play.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public long TotalScore
	{
		get;
		set => field = value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "The value must not be negative.");
	}

	/// <summary>The cumulative score across every submitted play on ranked beatmaps.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public long RankedScore
	{
		get;
		set => field = value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "The value must not be negative.");
	}

	/// <summary>The number of plays submitted.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public int PlayCount
	{
		get;
		set => field = value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "The value must not be negative.");
	}

	/// <summary>Determines whether another record is the statistics of the same user in the same game mode.</summary>
	/// <param name="other">The statistics to compare against, or <see langword="null" />.</param>
	public bool Equals(UserStats? other)
	{
		if (other is null) return false;
		return UserId == other.UserId && Mode == other.Mode;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is UserStats other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return HashCode.Combine(UserId, Mode);
	}
}