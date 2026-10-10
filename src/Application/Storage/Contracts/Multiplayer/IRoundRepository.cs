using Basil.Domain.Multiplayer.Match;
using Basil.Domain.Multiplayer.Round;

namespace Basil.Application.Storage.Contracts.Multiplayer;

/// <summary>Stores the rounds played in matches.</summary>
public interface IRoundRepository
{
	/// <summary>Stores a round, adding it when its match has no round with its number and replacing that round otherwise.</summary>
	/// <param name="round">The round to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CreateOrUpdateAsync(Round round, CancellationToken cancellationToken = default);

	/// <summary>Lists the rounds of a match in the order they were played.</summary>
	/// <param name="match">The match whose rounds to list.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The rounds of the match.</returns>
	Task<IReadOnlyList<Round>> ListAsync(Match match, CancellationToken cancellationToken = default);
}