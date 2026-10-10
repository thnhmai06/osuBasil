using Basil.Domain.Multiplayer.Match;

namespace Basil.Application.Storage.Contracts.Multiplayer;

/// <summary>Gets the results of a match, round by round.</summary>
public interface IMatchReportRepository
{
	/// <summary>Gets the results of a match, round by round.</summary>
	/// <param name="match">The match to report.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The report of the match.</returns>
	/// <remarks>The report reflects every round and score stored for the match.</remarks>
	ValueTask<MatchReport> GetAsync(Match match, CancellationToken cancellationToken = default);
}