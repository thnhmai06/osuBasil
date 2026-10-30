using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Persistence;

/// <summary>Stores matches.</summary>
public interface IMatchRepository
{
	/// <summary>Stores a new match and assigns its id.</summary>
	/// <param name="data">The data of the new match.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored match.</returns>
	Task<MatchRecord> CreateAsync(MatchData data, CancellationToken cancellationToken = default);

	/// <summary>Stores the new values of a created match, replacing the stored ones.</summary>
	/// <param name="match">The match to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task UpdateAsync(MatchRecord match, CancellationToken cancellationToken = default);

	/// <summary>Gets a match by id.</summary>
	/// <param name="id">The match id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The match, or <see langword="null" /> when no match has that id.</returns>
	ValueTask<MatchRecord?> GetAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>Lists the matches a query includes, newest first.</summary>
	/// <param name="query">Which matches to include.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>A page of matches.</returns>
	Task<Page<MatchRecord>> ListAsync(MatchQuery query, PageRequest page,
		CancellationToken cancellationToken = default);

	/// <summary>Tells whether a user may view a match's history and report.</summary>
	/// <param name="match">The match.</param>
	/// <param name="viewer">The user asking.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> when the match is public, or when the user took part in it or manages it (its
	///     creator, or a referee that is still one); this is the rule <see cref="MatchQuery.VisibleTo" /> applies to a
	///     listing. A permission that lets a user view every private match is not considered here.
	/// </returns>
	Task<bool> IsVisibleToAsync(MatchRecord match, UserRecord viewer, CancellationToken cancellationToken = default);
}