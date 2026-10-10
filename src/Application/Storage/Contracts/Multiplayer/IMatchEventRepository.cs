using Basil.Domain.Multiplayer.Match;

namespace Basil.Application.Storage.Contracts.Multiplayer;

/// <summary>Stores what happened in matches.</summary>
public interface IMatchEventRepository
{
	/// <summary>Records an event of a match.</summary>
	/// <param name="matchEvent">The event to record.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CreateAsync(MatchEvent matchEvent, CancellationToken cancellationToken = default);

	/// <summary>Lists the events of a match in the order they happened.</summary>
	/// <param name="match">The match whose events to list.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The events of the match.</returns>
	Task<IReadOnlyList<MatchEvent>> ListAsync(Match match, CancellationToken cancellationToken = default);
}