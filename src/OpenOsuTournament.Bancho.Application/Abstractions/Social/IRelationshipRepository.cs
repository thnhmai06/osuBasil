namespace OpenOsuTournament.Bancho.Application.Abstractions.Social;

/// <summary>Ported from app/repositories/relationships.py's RelationshipType (StrEnum).</summary>
public enum RelationshipType
{
    Friend,
    Block
}

/// <summary>Ported from app/repositories/relationships.py's Relationship dataclass.</summary>
public sealed record Relationship(int User1, int User2, RelationshipType Type);

/// <summary>Ported from app/repositories/relationships.py's RelationshipsRepository.</summary>
public interface IRelationshipRepository
{
    Task<Relationship> CreateAsync(int user1, int user2, RelationshipType type,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Relationship>> FetchAllAsync(int user1, RelationshipType? type = null,
        CancellationToken cancellationToken = default);

    Task<Relationship?> FetchOneAsync(int user1, int user2, CancellationToken cancellationToken = default);

    Task DeleteAsync(int user1, int user2, CancellationToken cancellationToken = default);
}