using Basil.Domain.Users;

namespace Basil.Domain.Social;

/// <summary>
///     A social relationship between two users.
/// </summary>
public sealed class Relationship : IEquatable<Relationship>
{
	/// <summary>The user who holds the relationship.</summary>
	public required User Actor { get; init; }

	/// <summary>The user the relationship is held toward.</summary>
	public required User Target { get; init; }

	/// <summary>The kind of relationship.</summary>
	public required RelationshipType Type { get; init; }

	public DateTimeOffset Since { get; init; } = DateTimeOffset.UtcNow;

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