using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>Builds reports from the rounds and scores of each match.</summary>
internal sealed class MatchReportRepository(IRoundRepository rounds, IMatchEventRepository events)
	: IMatchReportRepository
{
	/// <inheritdoc />
	public async ValueTask<MatchReport> GetAsync(MatchRecord match, CancellationToken cancellationToken = default)
	{
		var matchRounds = await rounds.ListAsync(match, cancellationToken);
		var matchEvents = await events.ListAsync(match, cancellationToken);
		return new MatchReport(match,
			[.. matchRounds.Cast<IMatchEntry>().Concat(matchEvents).OrderBy(record => record.Timestamp)]);
	}
}