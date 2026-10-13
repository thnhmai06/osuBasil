using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Multiplayer.Matches;

namespace Basil.Application.Storage.Contracts.Multiplayer.Matches;

/// <summary>Stores matches.</summary>
public interface IMatchRepository
{
	/// <summary>Stores a new match and assigns its id.</summary>
	/// <param name="data">The data of the new match.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored match.</returns>
	Task<Match> CreateAsync(MatchData data, CancellationToken cancellationToken = default);

	/// <summary>Stores the new values of a created match, replacing the stored ones.</summary>
	/// <param name="match">The match to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task UpdateAsync(Match match, CancellationToken cancellationToken = default);

	/// <summary>Gets a match by id.</summary>
	/// <param name="id">The match id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The match, or <see langword="null" /> when no match has that id.</returns>
	ValueTask<Match?> GetAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>Lists the matches a query includes, newest first.</summary>
	/// <param name="query">Which matches to include.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>A page of matches.</returns>
	Task<Page<Match>> ListAsync(MatchQuery query, PageRequest page, CancellationToken cancellationToken = default);
}