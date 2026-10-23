using Basil.Domain.Multiplayer;

namespace Basil.Application.Persistence.Multiplayer;

/// <summary>Stores the rounds played in matches.</summary>
public interface IRoundRepository
{
	/// <summary>Stores a round, adding it when its match has no round with its number and replacing that round otherwise.</summary>
	/// <remarks>The task completes once the round is stored.</remarks>
	/// <param name="round">The round to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CreateOrUpdateAsync(Round round, CancellationToken cancellationToken = default);

	/// <summary>Lists the rounds of a match in the order they were played.</summary>
	/// <remarks>A round that is in use elsewhere is returned as that same instance.</remarks>
	/// <param name="match">The match whose rounds to list.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The rounds of the match.</returns>
	Task<IReadOnlyList<Round>> ListAsync(MatchRecord match, CancellationToken cancellationToken = default);
}