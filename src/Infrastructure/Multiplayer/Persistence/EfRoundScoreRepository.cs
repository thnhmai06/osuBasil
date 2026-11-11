using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Scores.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>Stores which round each score belongs to.</summary>
internal sealed class EfRoundScoreRepository(DatabaseSession database, IScoreRepository scores)
	: IRoundScoreRepository
{
	/// <inheritdoc />
	public async Task<RoundScore> RecordAsync(RoundScore roundScore)
	{
		var round = roundScore.Round;
		var row = new RoundScoreRow
		{
			ScoreId = roundScore.Score.Id,
			MatchId = round.Match.Id,
			RoundNumber = round.Index,
			Team = roundScore.Team is { } team ? (int)team : null
		};
		await database.WriteAsync((MultiplayerDbContext context) =>
			context.UpsertAsync(row, stored => stored.ScoreId == row.ScoreId));
		return roundScore;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<RoundScore>> ListAsync(Round round, CancellationToken ct = default)
	{
		var rows = await database.ReadAsync((MultiplayerDbContext context) => context.RoundScores
			.Where(rs => rs.MatchId == round.Match.Id && rs.RoundNumber == round.Index)
			.OrderBy(rs => rs.ScoreId)
			.Take(ListLimits.Ceiling)
			.ToListAsync(ct), ct);

		var found = (await scores.GetManyAsync([.. rows.Select(row => row.ScoreId)], ct))
			.ToDictionary(score => score.Id);
		return
		[
			.. rows.Where(row => found.ContainsKey(row.ScoreId)).Select(row =>
				new RoundScore(found[row.ScoreId], round, row.Team is { } team ? (GameTeam)team : null))
		];
	}
}