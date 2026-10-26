using Basil.Application.Persistence.Multiplayer;
using Basil.Application.Persistence.Scores;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Multiplayer;

/// <summary>Stores which round each score belongs to.</summary>
internal sealed class PostgresRoundScoreRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	IScoreRepository scores) : IRoundScoreRepository
{
	/// <inheritdoc />
	public async Task<RoundScore> RecordAsync(RoundScore roundScore, CancellationToken cancellationToken = default)
	{
		var round = roundScore.Round;
		var row = new RoundScoreRow
		{
			ScoreId = roundScore.Score.Id,
			MatchId = round.Match.Id,
			RoundNumber = round.Index,
			Team = roundScore.Team is { } team ? (int)team : null
		};
		await writer.SaveAsync(Root.Match(row.MatchId), (GetType(), row.ScoreId), new WriteCommand((context, token) =>
			context.UpsertAsync(row, stored => stored.ScoreId == row.ScoreId, token)));
		return roundScore;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<RoundScore>> ListAsync(Round round, CancellationToken cancellationToken = default)
	{
		var rows = await reader.ReadAsync(context => context.RoundScores
			.Where(rs => rs.MatchId == round.Match.Id && rs.RoundNumber == round.Index)
			.OrderBy(rs => rs.ScoreId)
			.ToListAsync(cancellationToken), cancellationToken);

		var result = new List<RoundScore>(rows.Count);
		foreach (var row in rows)
			if (await scores.GetAsync(row.ScoreId, cancellationToken) is { } score)
				result.Add(new RoundScore(score, round, row.Team is { } team ? (GameTeam)team : null));
		return result;
	}
}
