using Basil.Shared.Utilities;

namespace Basil.Domain.Users;

public sealed class Relationship : IEquatable<Relationship>
{
	public required UserRecord Actor
	{
		get;
		init => field = value.Equals(Target)
			? throw new ArgumentException("Actor and Target cannot be the same user.", nameof(value))
			: value;
	}

	public required UserRecord Target
	{
		get;
		init => field = value.Equals(Actor)
			? throw new ArgumentException("Actor and Target cannot be the same user.", nameof(value))
			: value;
	}

	public required RelationshipType Type
	{
		get;
		init => field = value.ThrowIfInvalid();
	}

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

public enum RelationshipType : byte
{
	Friend,

	Block
}