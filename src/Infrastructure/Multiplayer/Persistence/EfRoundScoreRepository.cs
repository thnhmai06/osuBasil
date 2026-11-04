using Basil.Application.Common;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Scores.Persistence;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>Stores which round each score belongs to.</summary>
internal sealed class EfRoundScoreRepository(DatabaseSession database, IScoreRepository scores)
	: IRoundScoreRepository
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
		await database.WriteAsync((MultiplayerDbContext context, CancellationToken token) =>
			context.UpsertAsync(row, stored => stored.ScoreId == row.ScoreId, token), cancellationToken);
		return roundScore;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<RoundScore>> ListAsync(Round round, CancellationToken cancellationToken = default)
	{
		var rows = await database.ReadAsync((MultiplayerDbContext context) => context.RoundScores
			.Where(rs => rs.MatchId == round.Match.Id && rs.RoundNumber == round.Index)
			.OrderBy(rs => rs.ScoreId)
			.Take(ListLimits.Ceiling)
			.ToListAsync(cancellationToken), cancellationToken);

		var result = new List<RoundScore>(rows.Count);
		foreach (var row in rows)
			if (await scores.GetAsync(row.ScoreId, cancellationToken) is { } score)
				result.Add(new RoundScore(score, round, row.Team is { } team ? (GameTeam)team : null));
		return result;
	}
}
