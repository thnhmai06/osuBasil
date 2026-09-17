using Basil.Domain.Users;

namespace Basil.Domain.Social;

/// <summary>
///     A social relationship between two users.
/// </summary>
/// <param name="Type">The kind of relationship.</param>
public sealed record Relationship(User Actor, User Target, RelationshipType Type);

/// <summary>
///     The kind of social relationship one user can hold toward another.
/// </summary>
public enum RelationshipType : byte
{
	Friend,
	Block
}