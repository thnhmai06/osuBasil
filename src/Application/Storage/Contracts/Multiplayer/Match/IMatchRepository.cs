using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Multiplayer.Match;

namespace Basil.Application.Storage.Contracts.Multiplayer.Match;

/// <summary>Stores matches.</summary>
public interface IMatchRepository
{
	/// <summary>Stores a new match and assigns its id.</summary>
	/// <param name="data">The data of the new match.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored match.</returns>
	Task<Domain.Multiplayer.Match.Match> CreateAsync(MatchData data, CancellationToken cancellationToken = default);

	/// <summary>Stores a match under its id, adding it when no match has that id and replacing the stored match otherwise.</summary>
	/// <param name="match">The match to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CreateOrUpdateAsync(Domain.Multiplayer.Match.Match match, CancellationToken cancellationToken = default);

	/// <summary>Gets a match by id.</summary>
	/// <param name="id">The match id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The match, or <see langword="null" /> when no match has that id.</returns>
	ValueTask<Domain.Multiplayer.Match.Match?> GetAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>Lists the matches a query includes, newest first.</summary>
	/// <param name="query">Which matches to include.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>A page of matches.</returns>
	Task<Page<Domain.Multiplayer.Match.Match>> ListAsync(MatchQuery query, PageRequest page, CancellationToken cancellationToken = default);
}