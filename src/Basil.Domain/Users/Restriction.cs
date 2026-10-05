using Basil.Domain.Utilities;

namespace Basil.Domain.Users;

/// <summary>A restriction of a user, identified by its id.</summary>
public sealed class Restriction : IWrapper<RestrictionData>, IEquatable<Restriction>
{
	/// <summary>Gets the unique identifier of the restriction.</summary>
	public required int Id { get; init; }

	/// <summary>Gets the restriction data this identity wraps.</summary>
	public required RestrictionData Value { get; init; }

	/// <summary>Determines whether another restriction is the same record.</summary>
	/// <param name="other">The restriction to compare against, or <see langword="null" />.</param>
	/// <returns><see langword="true" /> if <paramref name="other" /> has the same <see cref="Id" />.</returns>
	public bool Equals(Restriction? other)
	{
		return other is not null && Id == other.Id;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is Restriction other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return Id;
	}
}

/// <summary>A suspension of some of a user's permissions for a period.</summary>
public sealed class RestrictionData
{
	/// <summary>Gets the restricted user.</summary>
	public required User User { get; init; }

	/// <summary>Gets or sets the permissions suspended while the restriction is active.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is none or sets a bit no permission defines.</exception>
	public required Permissions Permissions
	{
		get;
		set
		{
			value.ThrowIfUndefined();
			if (value == Permissions.None)
				throw new ArgumentOutOfRangeException(nameof(value), value, "A restriction must suspend a permission.");
			field = value;
		}
	}

	/// <summary>Gets the moment the restriction starts.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is after <see cref="EndsAt" />.</exception>
	public required DateTimeOffset StartsAt
	{
		get;
		init
		{
			if (value > EndsAt)
				throw new ArgumentOutOfRangeException(nameof(value), value, "A restriction cannot start after it ends.");
			field = value;
		}
	}

	/// <summary>Gets or sets the moment the restriction ends, or <see langword="null" /> when it lasts until lifted.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is before <see cref="StartsAt" />.</exception>
	public DateTimeOffset? EndsAt
	{
		get;
		set
		{
			if (value < StartsAt)
				throw new ArgumentOutOfRangeException(nameof(value), value, "A restriction cannot end before it starts.");
			field = value;
		}
	}

	/// <summary>Gets a value that indicates whether the restriction is in force at a moment.</summary>
	/// <param name="now">The moment to check.</param>
	/// <returns><see langword="true" /> from <see cref="StartsAt" /> up to, but not including, <see cref="EndsAt" />.</returns>
	public bool IsActive(DateTimeOffset now)
	{
		return StartsAt <= now && (EndsAt is null || now < EndsAt);
	}
}
