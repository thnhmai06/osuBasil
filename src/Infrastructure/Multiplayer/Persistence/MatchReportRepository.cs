using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>Builds reports from the rounds and scores of each match.</summary>
internal sealed class MatchReportRepository(
	IMatchRepository matches,
	IRoundRepository rounds,
	IMatchEventRepository events) : IMatchReportRepository
{
	/// <inheritdoc />
	public async ValueTask<MatchReport> GetAsync(MatchRecord match, CancellationToken cancellationToken = default)
	{
		// The report shows the match as it is stored now, not the copy the caller read earlier.
		match = await matches.GetAsync(match.Id, cancellationToken) ?? match;
		var matchRounds = await rounds.ListAsync(match, cancellationToken);
		var matchEvents = await events.ListAsync(match, cancellationToken);
		return new MatchReport(match,
			[.. matchRounds.Cast<IMatchEntry>().Concat(matchEvents).OrderBy(record => record.Timestamp)]);
	}
}