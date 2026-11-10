using Basil.Application.Common;
using Basil.Application.Scores.Persistence;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Scores.Persistence;

/// <summary>Stores submitted scores.</summary>
internal sealed class EfScoreRepository(DatabaseSession database) : IScoreRepository
{
	/// <inheritdoc />
	public async Task<ScoreRecord> CreateAsync(ScoreData data, CancellationToken cancellationToken = default)
	{
		var row = ToRow(0, data);
		await database.WriteAsync(async (ScoresDbContext context, CancellationToken token) =>
		{
			row.Id = (int)await IdCounters.NextAsync(context, "score");
			context.Scores.Add(row);
		}, cancellationToken);
		return new ScoreRecord { Id = row.Id, Value = data };
	}

	/// <inheritdoc />
	public async Task<(ScoreRecord Score, UserStats Stats)> CreateWithPlayAsync(ScoreData data, long rankedScore,
		CancellationToken cancellationToken = default)
	{
		try
		{
			return await StoreAsync(data, rankedScore, cancellationToken);
		}
		catch (AlreadyExistsException)
		{
			// Either another first play of the same account and mode created the statistics row between our update
			// and our insert, which a second attempt finds, or the score is a duplicate and the second attempt refuses it again.
			return await StoreAsync(data, rankedScore, cancellationToken);
		}
	}

	/// <inheritdoc />
	public async ValueTask<ScoreRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		var row = await database.ReadAsync((ScoresDbContext context) => context.Scores
			.Where(score => score.Id == id)
			.FirstOrDefaultAsync(cancellationToken), cancellationToken);
		return row is null ? null : ToScore(row);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<ScoreRecord>> GetManyAsync(IReadOnlyCollection<int> ids,
		CancellationToken cancellationToken = default)
	{
		var wanted = ids.ToArray();
		var rows = await database.ReadAsync((ScoresDbContext context) => context.Scores
			.Where(score => wanted.Contains(score.Id))
			.OrderBy(score => score.Id)
			.Take(ListLimits.Ceiling)
			.ToListAsync(cancellationToken), cancellationToken);
		return [.. rows.Select(ToScore)];
	}

	/// <inheritdoc />
	public async Task<Page<ScoreRecord>> ListAsync(ScoreQuery scoreQuery, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var fetched = await database.ReadAsync(async (ScoresDbContext context) =>
		{
			var matching = BuildFilter(context.Scores, scoreQuery);
			// A player's best play is the highest score; the earlier play and then the lower id win a tie.
			var q = scoreQuery.BestPerPlayer
				? matching.Where(s => !matching.Any(other => other.UserId == s.UserId &&
				                                             (other.TotalScore > s.TotalScore ||
				                                              (other.TotalScore == s.TotalScore &&
				                                               (other.Timestamp < s.Timestamp ||
				                                                (other.Timestamp == s.Timestamp && other.Id < s.Id))))))
				: matching;
			q = scoreQuery.BestPerPlayer || scoreQuery.BeatmapHash is not null
				? q.OrderByDescending(s => s.TotalScore).ThenBy(s => s.Timestamp).ThenBy(s => s.Id)
				: q.OrderByDescending(s => s.Timestamp).ThenByDescending(s => s.Id);

			return await q
				.Skip(page.Offset)
				.Take(page.Fetch)
				.ToListAsync(cancellationToken);
		}, cancellationToken);

		var (rows, hasNext) = page.Slice(fetched);
		return new Page<ScoreRecord>(rows.Select(ToScore).ToList(), hasNext);
	}

	private async Task<(ScoreRecord Score, UserStats Stats)> StoreAsync(ScoreData data, long rankedScore,
		CancellationToken cancellationToken)
	{
		var row = ToRow(0, data);
		UserStats stats = null!;
		await database.TransactAsync(async (ScoresDbContext context, CancellationToken token) =>
		{
			row.Id = (int)await IdCounters.NextAsync(context, "score");
			context.Scores.Add(row);
			stats = await EfUserStatsRepository.AddPlayAsync(context, data.UserId!.Value, data.Mode,
				data.LastState.TotalScore, rankedScore, token);
		}, cancellationToken);
		return (new ScoreRecord { Id = row.Id, Value = data }, stats);
	}

	private static IQueryable<ScoreRow> BuildFilter(IQueryable<ScoreRow> q, ScoreQuery scoreQuery)
	{
		if (scoreQuery.Player is { } player)
			q = q.Where(s => s.UserId == player.Id);

		if (scoreQuery.BeatmapHash is { } beatmapHash)
			q = q.Where(s => s.BeatmapHash == beatmapHash.HashValue);

		if (scoreQuery.Mods is { } mods)
			q = q.Where(s => s.Mods == (int)mods);

		if (scoreQuery.Players is { } players)
		{
			var playerIds = players.Select(p => p.Id).ToList();
			q = q.Where(s => s.UserId != null && playerIds.Contains(s.UserId.Value));
		}

		if (scoreQuery.BestPerPlayer)
			q = q.Where(s => s.UserId != null);

		return q;
	}

	/// <summary>Makes the row that stores a score, with the values it has now; the id is 0 for a score not stored yet.</summary>
	private static ScoreRow ToRow(int id, ScoreData data)
	{
		return new ScoreRow
		{
			Id = id,
			UserId = data.UserId,
			BeatmapHash = data.BeatmapHash?.HashValue,
			Mode = (int)data.Mode,
			Mods = (int)data.Mods,
			Num300 = data.LastState.HitCounts.Num300,
			Num100 = data.LastState.HitCounts.Num100,
			Num50 = data.LastState.HitCounts.Num50,
			NumGeki = data.LastState.HitCounts.NumGeki,
			NumKatu = data.LastState.HitCounts.NumKatu,
			NumMiss = data.LastState.HitCounts.NumMiss,
			TotalScore = data.LastState.TotalScore,
			MaxCombo = data.LastState.MaxCombo,
			CurrentCombo = data.LastState.CurrentCombo,
			Grade = (int)data.Grade,
			IsPassed = data.IsPassed,
			IsFullCombo = data.LastState.IsFullCombo,
			Timestamp = data.Timestamp.ToUniversalTime(),
			Checksum = data.Checksum?.HashValue
		};
	}

	private static ScoreRecord ToScore(ScoreRow row)
	{
		return new ScoreRecord { Id = row.Id, Value = ToData(row) };
	}

	private static ScoreData ToData(ScoreRow row)
	{
		return new ScoreData(
				row.UserId,
				row.BeatmapHash is { } beatmapHash ? new Md5(beatmapHash) : (Md5?)null,
				(GameMode)row.Mode,
				(GameMods)row.Mods,
				new ScoreState(new HitCounts(row.Num300, row.Num100, row.Num50, row.NumGeki, row.NumKatu, row.NumMiss),
					row.TotalScore, (ushort)row.MaxCombo, (ushort)row.CurrentCombo, row.IsFullCombo),
				(Grade)row.Grade,
				row.IsPassed,
				row.Timestamp)
			with
			{
				Checksum = row.Checksum is { } checksum ? new Md5(checksum) : (Md5?)null
			};
	}
}