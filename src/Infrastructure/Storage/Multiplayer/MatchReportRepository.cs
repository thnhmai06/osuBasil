using Basil.Application.Storage.Contracts.Multiplayer.Matches;
using Basil.Domain.Multiplayer.Matches;
using Basil.Domain.Multiplayer.Rounds;

namespace Basil.Infrastructure.Storage.Multiplayer;

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
		var matchRounds = await rounds.ListAsync(match, cancellationToken).ConfigureAwait(false);
		foreach (var round in matchRounds)
			round.Result = RoundResult.Decide(round,
				await roundScores.ListAsync(round, cancellationToken).ConfigureAwait(false));

		var matchEvents = await events.ListAsync(match, cancellationToken).ConfigureAwait(false);
		return new MatchReport(match,
			[.. matchRounds.Cast<IMatchEntry>().Concat(matchEvents).OrderBy(record => record.Timestamp)]);
	}
}