using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Scores;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;

namespace Basil.Infrastructure.Storage.Multiplayer;

/// <summary>Builds reports from the rounds and scores of each match.</summary>
internal sealed class MatchReportRepository(
	IRoundRepository rounds,
	IScoreRepository scores,
	MatchReportCache reports) : IMatchReportRepository
{
	/// <inheritdoc />
	public async ValueTask<MatchReport> GetAsync(Match match, CancellationToken cancellationToken = default)
	{
		var report = await reports.Reports.GetOrAddAsync(match.Id,
			async _ => (MatchReport?)await BuildAsync(match, default), cancellationToken);
		return report ?? throw new InvalidOperationException("A match report load returned no report.");
	}

	private async Task<MatchReport> BuildAsync(Match match, CancellationToken cancellationToken)
	{
		var matchRounds = await rounds.ListAsync(match, cancellationToken);
		var matchScores = new List<Score>();
		const int pageSize = 500;
		for (var offset = 0; ; offset += pageSize)
		{
			var page = await scores.ListAsync(new ScoreQuery(Match: match), new PageRequest(offset, pageSize), cancellationToken);
			matchScores.AddRange(page.Items);
			if (matchScores.Count >= page.Total || page.Items.Count == 0)
				break;
		}

		var reportRounds = new List<MatchReportRound>(matchRounds.Count);
		foreach (var round in matchRounds)
		{
			var roundScores = matchScores.Where(score => score.Value.Round?.Number == round.Number).ToList();
			reportRounds.Add(new MatchReportRound(round, roundScores, RoundResult.Decide(round, roundScores)));
		}

		return new MatchReport(match, reportRounds);
	}
}
