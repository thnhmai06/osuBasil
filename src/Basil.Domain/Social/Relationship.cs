using Basil.Domain.Users;

namespace Basil.Domain.Social;

/// <summary>
///     A social relationship between two users.
/// </summary>
/// <param name="Actor">The user who holds the relationship.</param>
/// <param name="Target">The user the relationship is held toward.</param>
/// <param name="Type">The kind of relationship.</param>
public sealed record Relationship(User Actor, User Target, RelationshipType Type);

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