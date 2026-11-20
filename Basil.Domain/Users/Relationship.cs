using Basil.Shared.Utilities;

namespace Basil.Domain.Users;

/// <summary>A relationship that one user sets towards another, such as a friendship or a block.</summary>
/// <remarks>Two relationships are equal when their actor, target and type are equal.</remarks>
public sealed class Relationship : IEquatable<Relationship>
{
	/// <summary>The user who set the relationship.</summary>
	/// <exception cref="ArgumentException">The value is the same user as <see cref="Target" />.</exception>
	public required UserRecord Actor
	{
		get;
		init => field = value.Equals(Target)
			? throw new ArgumentException("Actor and Target cannot be the same user.", nameof(value))
			: value;
	}

	/// <summary>The user the relationship points at.</summary>
	/// <exception cref="ArgumentException">The value is the same user as <see cref="Actor" />.</exception>
	public required UserRecord Target
	{
		get;
		init => field = value.Equals(Actor)
			? throw new ArgumentException("Actor and Target cannot be the same user.", nameof(value))
			: value;
	}

	/// <summary>The kind of relationship.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is not a defined kind.</exception>
	public required RelationshipType Type
	{
		get;
		init => field = value.ThrowIfInvalid();
	}

	/// <summary>The moment the relationship was set.</summary>
	public required DateTimeOffset CreatedAt { get; init; }

	public bool Equals(Relationship? other)
	{
		if (other is null) return false;
		return Actor.Equals(other.Actor) && Target.Equals(other.Target) && Type == other.Type;
	}

	public override bool Equals(object? obj)
	{
		return obj is Relationship other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Actor, Target, Type);
	}
}

/// <summary>The kind of a relationship between two users.</summary>
public enum RelationshipType : byte
{
	/// <summary>The actor lists the target as a friend.</summary>
	Friend,

	/// <summary>The actor blocks the target.</summary>
	Block
}