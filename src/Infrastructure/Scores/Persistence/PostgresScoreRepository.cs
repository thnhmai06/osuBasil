using Basil.Application.Common;
using Basil.Application.Scores.Persistence;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Infrastructure.Common.Memory;
using Basil.Infrastructure.Common.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Scores.Persistence;

/// <summary>Stores submitted scores.</summary>
internal sealed class PostgresScoreRepository(
	DatabaseReader reader,
	DatabaseWriter writer) : MemoryRepository<int, ScoreRecord>(reader, writer), IScoreRepository
{
	private readonly WeakIndex<Md5, ScoreRecord> _byChecksum = new();

	/// <inheritdoc />
	public async Task<ScoreRecord> CreateAsync(ScoreData data, CancellationToken cancellationToken = default)
	{
		if (data.Checksum is { } checksum && _byChecksum.TryGetValue(checksum, out var known) &&
		    known.Value.Checksum == checksum)
			throw new AlreadyExistsException($"A score with checksum {checksum} is already stored.");

		int id;
		try
		{
			id = await Writer.WriteAsync(Root.User(data.UserId ?? 0),
				async (ScoresDbContext context, CancellationToken token) =>
				{
					var row = ToRow(0, data);
					context.Scores.Add(row);
					await context.SaveChangesAsync(token);
					return row.Id;
				}, cancellationToken);
		}
		catch (Exception exception) when (exception.IsUniqueViolation())
		{
			throw new AlreadyExistsException("A score with the same checksum is already stored.");
		}

		var score = Track(new ScoreRecord { Id = id, Value = data });
		IndexScore(score);
		return score;
	}

	/// <inheritdoc />
	public async ValueTask<ScoreRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		var score = await FindAsync(id, cancellationToken);
		if (score is not null)
			IndexScore(score);
		return score;
	}

	/// <inheritdoc />
	public async Task<Page<ScoreRecord>> ListAsync(ScoreQuery scoreQuery, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var (total, rows) = await Reader.ReadAsync(async (ScoresDbContext context) =>
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
			var count = await q.CountAsync(cancellationToken);
			q = scoreQuery.BestPerPlayer || scoreQuery.BeatmapHash is not null
				? q.OrderByDescending(s => s.TotalScore).ThenBy(s => s.Timestamp).ThenBy(s => s.Id)
				: q.OrderByDescending(s => s.Timestamp).ThenByDescending(s => s.Id);

			var rows = await q
				.Skip(page.Offset)
				.Take(page.Limit)
				.ToListAsync(cancellationToken);
			return (count, rows);
		}, cancellationToken);

		var items = rows.Select(row => Track(ToScore(row))).ToList();
		foreach (var score in items)
			IndexScore(score);
		return new Page<ScoreRecord>(items, total);
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

	protected override int KeyOf(ScoreRecord item)
	{
		return item.Id;
	}

	protected override Root RootOf(ScoreRecord item)
	{
		return Root.User(item.Value.UserId ?? 0);
	}

	protected override async Task<ScoreRecord?> LoadAsync(int key)
	{
		var row = await Reader.ReadAsync((ScoresDbContext context) => context.Scores
			.Where(row => row.Id == key)
			.FirstOrDefaultAsync(CancellationToken.None));
		return row is null ? null : ToScore(row);
	}

	protected override WriteCommand<ScoresDbContext> SaveCommand(ScoreRecord score)
	{
		var row = ToRow(score.Id, score.Value);
		return new WriteCommand<ScoresDbContext>((context, token) =>
			context.ReplaceAsync(row, stored => stored.Id == row.Id, token));
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
			Num300 = data.HitCounts.Num300,
			Num100 = data.HitCounts.Num100,
			Num50 = data.HitCounts.Num50,
			NumGeki = data.HitCounts.NumGeki,
			NumKatu = data.HitCounts.NumKatu,
			NumMiss = data.HitCounts.NumMiss,
			TotalScore = data.TotalScore,
			MaxCombo = data.MaxCombo,
			CurrentCombo = data.CurrentCombo,
			Grade = (int)data.Grade,
			IsPassed = data.IsPassed,
			IsFullCombo = data.IsFullCombo,
			Timestamp = data.Timestamp.ToUniversalTime(),
			Checksum = data.Checksum?.HashValue
		};
	}

	private void IndexScore(ScoreRecord score)
	{
		if (score.Value.Checksum is { } checksum)
			_byChecksum.Set(checksum, score);
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
				new HitCounts(row.Num300, row.Num100, row.Num50, row.NumGeki, row.NumKatu, row.NumMiss),
				row.TotalScore,
				(short)row.MaxCombo,
				(short)row.CurrentCombo,
				(Grade)row.Grade,
				row.IsPassed,
				row.IsFullCombo,
				row.Timestamp)
			with
			{
				Checksum = row.Checksum is { } checksum ? new Md5(checksum) : (Md5?)null
			};
	}
}