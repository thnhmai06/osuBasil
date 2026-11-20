using Basil.Domain.Game;
using Basil.Shared.Utilities;

namespace Basil.Domain.Users;

/// <summary>A user's statistics in one game mode.</summary>
/// <remarks>Two statistics are equal when they belong to the same user and mode.</remarks>
public sealed class UserStats : IEquatable<UserStats>
{
	/// <summary>The user's accuracy as a percentage; it is a fixed 100 until the server calculates it.</summary>
	public static double Accuracy => 100.0;

	/// <summary>The user's performance points; it is a fixed 727 until the server calculates it.</summary>
	public static int Pp => 727;

	/// <summary>The user's rank in the mode; it is the user's identifier until the server calculates ranks.</summary>
	public int Rank => UserId;

	/// <summary>The identifier of the user the statistics belong to.</summary>
	public required int UserId { get; init; }

	/// <summary>The game mode the statistics are for.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is not a defined game mode.</exception>
	public required GameMode Mode
	{
		get;
		init => field = value.ThrowIfInvalid();
	}

	/// <summary>The sum of the scores of every play.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public long TotalScore
	{
		get;
		set => field = value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "The value must not be negative.");
	}

	/// <summary>The sum of the best scores on each ranked beatmap.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public long RankedScore
	{
		get;
		set => field = value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "The value must not be negative.");
	}

	/// <summary>The number of plays.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public int PlayCount
	{
		get;
		set => field = value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "The value must not be negative.");
	}

	public bool Equals(UserStats? other)
	{
		if (other is null) return false;
		return UserId == other.UserId && Mode == other.Mode;
	}

	public override bool Equals(object? obj)
	{
		return obj is UserStats other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(UserId, Mode);
	}
}