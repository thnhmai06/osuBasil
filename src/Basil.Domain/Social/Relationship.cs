namespace Basil.Domain.Social;

/// <summary>
///     A social relationship between two users.
/// </summary>
/// <param name="User1">The id of the user who owns the relationship.</param>
/// <param name="User2">The id of the user the relationship points at.</param>
/// <param name="Type">The kind of relationship.</param>
public sealed record Relationship(int User1, int User2, RelationshipType Type);

/// <summary>
///     The kind of social relationship one user can hold toward another.
/// </summary>
public enum RelationshipType : byte
{
	Friend,
	Block
}