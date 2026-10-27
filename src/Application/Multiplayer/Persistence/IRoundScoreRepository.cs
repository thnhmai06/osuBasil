using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Persistence;

/// <summary>Stores which round each score belongs to.</summary>
public interface IRoundScoreRepository
{
	/// <summary>Stores the relation between a round and a score.</summary>
	/// <param name="roundScore">The round, the score, and the team the score was set for.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The relation, which is stored in the background; a listing right after includes it.</returns>
	Task<RoundScore> RecordAsync(RoundScore roundScore, CancellationToken cancellationToken = default);

	/// <summary>Lists the scores of a round, in submission order.</summary>
	/// <param name="round">The round.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The round's scores.</returns>
	Task<IReadOnlyList<RoundScore>> ListAsync(Round round, CancellationToken cancellationToken = default);
}