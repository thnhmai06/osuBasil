using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Scores;
using Basil.Domain.Multiplayer;

namespace Basil.Infrastructure.Storage.Multiplayer;

/// <summary>Builds reports from the rounds and scores of each match.</summary>
internal sealed class MatchReportRepository(
	IRoundRepository rounds,
	IScoreRepository scores) : IMatchReportRepository
{
	/// <inheritdoc />
	public async ValueTask<MatchReport> GetAsync(Match match, CancellationToken cancellationToken = default)
	{
		var matchRounds = await rounds.ListAsync(match, cancellationToken);
		var reportRounds = new List<MatchReportRound>(matchRounds.Count);
		foreach (var round in matchRounds)
		{
			var roundScores = await scores.ListAsync(round, cancellationToken);
			reportRounds.Add(new MatchReportRound(round, roundScores, RoundResult.Decide(round, roundScores)));
		}

		return new MatchReport(match, reportRounds);
	}
}