using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer.Report;
using Basil.Domain.Multiplayer.Report.Matches;

namespace Basil.Infrastructure.Multiplayer.Persistence;

internal sealed class MatchReportRepository(
	IMatchRepository matches,
	IRoundRepository rounds,
	IMatchEventRepository events) : IMatchReportRepository
{
	public async ValueTask<MatchReport> GetAsync(MatchRecord match, CancellationToken ct = default)
	{
		// The report shows the match as it is stored now, not the copy the caller read earlier.
		match = await matches.GetAsync(match.Id, ct) ?? match;
		var matchRounds = await rounds.ListAsync(match, ct);
		var matchEvents = await events.ListAsync(match, ct);
		return new MatchReport(match,
			[.. matchRounds.Cast<IMatchEntry>().Concat(matchEvents).OrderBy(record => record.Timestamp)]);
	}
}