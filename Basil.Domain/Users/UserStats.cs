using Basil.Domain.Mechanics;
using Basil.Shared.Utilities;

namespace Basil.Domain.Users;

public sealed class UserStats : IEquatable<UserStats>
{
	public static double Accuracy => 100.0;

	public static int Pp => 727;

	public int Rank => UserId;

	public required int UserId { get; init; }

	public required GameMode Mode
	{
		get;
		init => field = value.ThrowIfInvalid();
	}

	public long TotalScore
	{
		get;
		set => field = value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "The value must not be negative.");
	}

	public long RankedScore
	{
		get;
		set => field = value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "The value must not be negative.");
	}

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