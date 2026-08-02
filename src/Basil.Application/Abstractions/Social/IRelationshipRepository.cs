using Basil.Domain.Social;

namespace Basil.Application.Abstractions.Social;

/// <summary>
///     Provides access to the Relationships table.
/// </summary>
public interface IRelationshipRepository
{
	/// <summary>
	///     Creates a relationship between two users and returns it as persisted.
	/// </summary>
	/// <param name="user1">The id of the user who owns the relationship.</param>
	/// <param name="user2">The id of the user the relationship points at.</param>
	/// <param name="type">The kind of relationship to create.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The newly created relationship.</returns>
	Task<Relationship> CreateAsync(int user1, int user2, RelationshipType type,
		CancellationToken cancellationToken = default);

	/// <summary>
	///     Fetches every relationship a user owns.
	/// </summary>
	/// <param name="user1">The id of the user whose relationships to read.</param>
	/// <param name="type">An optional filter limiting results to one kind of relationship.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The user's relationships, optionally filtered by type.</returns>
	Task<IReadOnlyList<Relationship>> FetchAllAsync(int user1, RelationshipType? type = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	///     Fetches a single relationship between two users.
	/// </summary>
	/// <param name="user1">The id of the user who owns the relationship.</param>
	/// <param name="user2">The id of the user the relationship points at.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The matching relationship, or <see langword="null" /> when none exists.</returns>
	Task<Relationship?> FetchOneAsync(int user1, int user2, CancellationToken cancellationToken = default);

	/// <summary>
	///     Deletes the relationship between two users.
	/// </summary>
	/// <param name="user1">The id of the user who owns the relationship.</param>
	/// <param name="user2">The id of the user the relationship points at.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(int user1, int user2, CancellationToken cancellationToken = default);
}