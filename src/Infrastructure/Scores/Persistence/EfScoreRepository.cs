using Basil.Application.Scores.Persistence;
using Basil.Application.Shared.Exceptions;
using Basil.Application.Shared.Paging;
using Basil.Domain.Game.Mechanics;
using Basil.Domain.Game.Scores;
using Basil.Domain.Users;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Shared.Structures.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Scores.Persistence;

internal sealed class EfScoreRepository(DatabaseSession database) : IScoreRepository
{
	public async Task<ScoreRecord> CreateAsync(ScoreData data)
	{
		var row = ToRow(0, data);
		await database.WriteAsync(async (ScoresDbContext context) =>
		{
			row.Id = (int)await IdCounters.NextAsync(context, "score");
			context.Scores.Add(row);
		});
		return new ScoreRecord { Id = row.Id, Value = data };
	}

	public async Task<(ScoreRecord Score, UserStats Stats)> CreateWithPlayAsync(ScoreData data, long rankedScore)
	{
		try
		{
			return await StoreAsync(data, rankedScore);
		}
		catch (AlreadyExistsException)
		{
			// Either another first play of the same account and mode created the statistics row between our update
			// and our insert, which a second attempt finds, or the score is a duplicate and the second attempt refuses it again.
			return await StoreAsync(data, rankedScore);
		}
	}

	public async ValueTask<ScoreRecord?> GetAsync(int id, CancellationToken ct = default)
	{
		var row = await database.ReadAsync((ScoresDbContext context) => context.Scores
			.Where(score => score.Id == id)
			.FirstOrDefaultAsync(ct), ct);
		return row is null ? null : ToScore(row);
	}

	public async Task<IReadOnlyList<ScoreRecord>> GetManyAsync(IReadOnlyCollection<int> ids,
		CancellationToken ct = default)
	{
		var wanted = ids.ToArray();
		var rows = await database.ReadAsync((ScoresDbContext context) => context.Scores
			.Where(score => wanted.Contains(score.Id))
			.OrderBy(score => score.Id)
			.Take(ListLimits.Ceiling)
			.ToListAsync(ct), ct);
		return [.. rows.Select(ToScore)];
	}

	public async Task<Page<ScoreRecord>> ListAsync(ScoreQuery scoreQuery, PageRequest page,
		CancellationToken ct = default)
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
				.ToListAsync(ct);
		}, ct);

		var (rows, hasNext) = page.Slice(fetched);
		return new Page<ScoreRecord>(rows.Select(ToScore).ToList(), hasNext);
	}

	public Task SetPpAsync(int id, double pp)
	{
		return database.WriteAsync((ScoresDbContext context) => context.Scores.Where(score => score.Id == id)
			.ExecuteUpdateAsync(setters => setters.SetProperty(score => score.Pp, pp)));
	}

	public async Task<IReadOnlyList<ScoreRecord>> ListForPpAsync(int afterId, bool onlyMissing, int limit,
		CancellationToken ct = default)
	{
		var rows = await database.ReadAsync((ScoresDbContext context) => context.Scores
			.Where(score => score.Id > afterId && score.IsPassed && (!onlyMissing || score.Pp == null))
			.OrderBy(score => score.Id)
			.Take(limit)
			.ToListAsync(ct), ct);
		return [.. rows.Select(ToScore)];
	}

	private async Task<(ScoreRecord Score, UserStats Stats)> StoreAsync(ScoreData data, long rankedScore)
	{
		var row = ToRow(0, data);
		UserStats stats = null!;
		await database.TransactAsync(async (ScoresDbContext context) =>
		{
			row.Id = (int)await IdCounters.NextAsync(context, "score");
			context.Scores.Add(row);
			stats = await EfUserStatsRepository.AddPlayAsync(context, data.UserId!.Value, data.Mode,
				data.LastState.TotalScore, rankedScore);
		});
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

		if (scoreQuery.UserIds is { } userIds)
		{
			var ids = userIds.ToList();
			q = q.Where(s => s.UserId != null && ids.Contains(s.UserId.Value));
		}

		if (scoreQuery.BeatmapHashes is { } hashes)
		{
			var values = hashes.Select(h => h.HashValue).ToList();
			q = q.Where(s => s.BeatmapHash != null && values.Contains(s.BeatmapHash));
		}

		if (scoreQuery.PassedOnly)
			q = q.Where(s => s.IsPassed);

		if (scoreQuery.Mode is { } mode)
			q = q.Where(s => s.Mode == (int)mode);

		if (scoreQuery.BestPerPlayer)
			q = q.Where(s => s.UserId != null);

		return q;
	}

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
			Checksum = data.Checksum?.HashValue,
			Pp = data.PerformancePoints
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
				row.BeatmapHash is { } beatmapHash ? new Md5Hash(beatmapHash) : (Md5Hash?)null,
				(GameMode)row.Mode,
				(GameMods)row.Mods,
				new ScoreState(new HitCounts(row.Num300, row.Num100, row.Num50, row.NumGeki, row.NumKatu, row.NumMiss),
					row.TotalScore, (ushort)row.MaxCombo, (ushort)row.CurrentCombo, row.IsFullCombo),
				(Grade)row.Grade,
				row.IsPassed,
				row.Timestamp)
			with
			{
				Checksum = row.Checksum is { } checksum ? new Md5Hash(checksum) : (Md5Hash?)null,
				PerformancePoints = row.Pp
			};
	}
}