using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Multiplayer.Match;
using Basil.Application.Storage.Contracts.Multiplayer.Room;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer.Match;
using Basil.Domain.Multiplayer.Round;

namespace Basil.Infrastructure.Storage.Multiplayer;

/// <summary>Builds reports from the rounds and scores of each match.</summary>
internal sealed class MatchReportRepository(IRoundRepository rounds, IRoundScoreRepository roundScores)
	: IMatchReportRepository
{
	/// <inheritdoc />
	public async ValueTask<MatchReport> GetAsync(Match match, CancellationToken cancellationToken = default)
	{
		var matchRounds = await rounds.ListAsync(match, cancellationToken);
		var reportRounds = new List<RoundReport>(matchRounds.Count);
		foreach (var round in matchRounds)
		{
			var scores = await roundScores.ListAsync(round, cancellationToken);
			RoundResult? result = scores.Count == 0
				? null
				: round.Settings.TeamType.IsTeamMode()
					? new TeamVersusResult(round, scores)
					: new HeadToHeadResult(round, scores);
			reportRounds.Add(new RoundReport(round, scores.Select(score => score.Score).ToList(), result));
		}

		return new MatchReport(match, reportRounds);
	}
}
