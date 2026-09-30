using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer;

/// <summary>Stores matches.</summary>
public interface IMatchRepository
{
	/// <summary>Stores a new match and assigns its id.</summary>
	/// <param name="data">The data of the new match.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	/// <returns>The stored match.</returns>
	Task<Match> AddAsync(MatchData data, CancellationToken cancellationToken = default);
}