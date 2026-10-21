using Basil.Application.Persistence.Multiplayer;
using Basil.Domain.Multiplayer;

namespace Basil.Infrastructure.Persistence.Multiplayer;

/// <summary>Builds reports from the rounds and scores of each match.</summary>
internal sealed class MatchReportRepository(
	IRoundRepository rounds,
	IRoundScoreRepository roundScores,
	IMatchEventRepository events)
	: IMatchReportRepository
{
	/// <inheritdoc />
	public async ValueTask<MatchReport> GetAsync(MatchRecord match, CancellationToken cancellationToken = default)
	{
		var matchRounds = await rounds.ListAsync(match, cancellationToken);
		foreach (var round in matchRounds)
			round.Result = RoundResult.Decide(round,
				await roundScores.ListAsync(round, cancellationToken));

		var matchEvents = await events.ListAsync(match, cancellationToken);
		return new MatchReport(match,
			[.. matchRounds.Cast<IMatchEntry>().Concat(matchEvents).OrderBy(record => record.Timestamp)]);
	}
}