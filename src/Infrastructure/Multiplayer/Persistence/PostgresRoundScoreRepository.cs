using System.Collections.Concurrent;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Scores.Persistence;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>Stores which round each score belongs to.</summary>
internal sealed class PostgresRoundScoreRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	IScoreRepository scores) : IRoundScoreRepository
{
	private readonly ConcurrentDictionary<int, RoundScore> _pending = new();

	/// <inheritdoc />
	public Task<RoundScore> RecordAsync(RoundScore roundScore, CancellationToken cancellationToken = default)
	{
		var round = roundScore.Round;
		var row = new RoundScoreRow
		{
			ScoreId = roundScore.Score.Id,
			MatchId = round.Match.Id,
			RoundNumber = round.Index,
			Team = roundScore.Team is { } team ? (int)team : null
		};
		_pending[row.ScoreId] = roundScore;
		var queued = writer.SaveAsync(Root.Match(row.MatchId), (GetType(), row.ScoreId),
			new WriteCommand<MultiplayerDbContext>((context, token) =>
				context.UpsertAsync(row, stored => stored.ScoreId == row.ScoreId, token)));
		_ = queued.ContinueWith(task => _pending.TryRemove(row.ScoreId, out _), TaskScheduler.Default);
		return Task.FromResult(roundScore);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<RoundScore>> ListAsync(Round round, CancellationToken cancellationToken = default)
	{
		// Taken before the database is read, so a relation whose write finishes during the read is in the snapshot or in
		// the rows, never in neither.
		var queued = _pending.Values.Where(pending => pending.Round.Equals(round)).ToList();
		var rows = await reader.ReadAsync((MultiplayerDbContext context) => context.RoundScores
			.Where(rs => rs.MatchId == round.Match.Id && rs.RoundNumber == round.Index)
			.OrderBy(rs => rs.ScoreId)
			.ToListAsync(cancellationToken), cancellationToken);

		var result = new List<RoundScore>(rows.Count);
		foreach (var row in rows)
			if (await scores.GetAsync(row.ScoreId, cancellationToken) is { } score)
				result.Add(new RoundScore(score, round, row.Team is { } team ? (GameTeam)team : null));

		// A relation whose write is still queued is not in the database yet.
		result.AddRange(queued.Where(pending => result.All(stored => stored.Score.Id != pending.Score.Id)));
		return [.. result.OrderBy(roundScore => roundScore.Score.Id)];
	}
}