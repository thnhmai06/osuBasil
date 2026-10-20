using Basil.Domain.Common;

namespace Basil.Domain.Users;

/// <summary>
///     A social relationship between two users.
/// </summary>
public sealed class Relationship : IEquatable<Relationship>
{
	/// <summary>The user who holds the relationship.</summary>
	/// <exception cref="ArgumentException">The value is the same user as <see cref="Target" />.</exception>
	public required UserRecord Actor
	{
		get;
		init => field = value.Equals(Target)
			? throw new ArgumentException("Actor and Target cannot be the same user.", nameof(value))
			: value;
	}

	/// <summary>The user the relationship is held toward.</summary>
	/// <exception cref="ArgumentException">The value is the same user as <see cref="Actor" />.</exception>
	public required UserRecord Target
	{
		get;
		init => field = value.Equals(Actor)
			? throw new ArgumentException("Actor and Target cannot be the same user.", nameof(value))
			: value;
	}

	/// <summary>The kind of relationship.</summary>
	public required RelationshipType Type
	{
		get;
		init => field = value.ThrowIfUndefined();
	}

	/// <summary>When the relationship was set.</summary>
	public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

	/// <summary>Determines whether another relationship has the same actor, target and type.</summary>
	/// <param name="other">The relationship to compare against, or <see langword="null" />.</param>
	public bool Equals(Relationship? other)
	{
		if (other is null) return false;
		return Actor.Equals(other.Actor) && Target.Equals(other.Target) && Type == other.Type;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is Relationship other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return HashCode.Combine(Actor, Target, Type);
	}
}

/// <summary>
///     The kind of social relationship one user can hold toward another.
/// </summary>
public enum RelationshipType : byte
{
	/// <summary>The actor and the target are friends.</summary>
	Friend,

	/// <summary>The actor blocks the target.</summary>
	Block
}