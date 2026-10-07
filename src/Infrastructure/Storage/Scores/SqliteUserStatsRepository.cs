using Basil.Application.Storage.Contracts.Scores;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Infrastructure.Storage.Batching;
using Basil.Infrastructure.Storage.Caching;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage.Scores;

/// <summary>Stores users' cumulative score statistics.</summary>
internal sealed class SqliteUserStatsRepository(DatabaseBatcher batcher)
	: CachedRepository<(int UserId, GameMode Mode), UserStats>(batcher), IUserStatsRepository
{
	protected override (int UserId, GameMode Mode) KeyOf(UserStats item) => (item.UserId, item.Mode);

	/// <inheritdoc />
	public async ValueTask<UserStats> GetAsync(User user, GameMode mode, CancellationToken cancellationToken = default)
	{
		var key = (user.Id, mode);
		return await FindAsync(key, cancellationToken)
			?? Track(new UserStats { UserId = user.Id, Mode = mode });
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(UserStats stats, CancellationToken cancellationToken = default)
	{
		var live = Track(stats);
		live.TotalScore = stats.TotalScore;
		live.RankedScore = stats.RankedScore;
		live.PlayCount = stats.PlayCount;
		return SaveAsync(live);
	}

	protected override async Task<UserStats?> LoadAsync((int UserId, GameMode Mode) key, CancellationToken cancellationToken)
	{
		var row = await Batcher.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<StatsRow>(
			"SELECT UserId, Mode, TotalScore, RankedScore, PlayCount FROM UserStats WHERE UserId = @UserId AND Mode = @Mode",
			new { UserId = key.UserId, Mode = (long)key.Mode }), cancellationToken);
		return row is null
			? null
			: new UserStats
			{
				UserId = row.UserId,
				Mode = (GameMode)row.Mode,
				TotalScore = row.TotalScore,
				RankedScore = row.RankedScore,
				PlayCount = row.PlayCount
			};
	}

	protected override string WriteSql =>
		"""
		INSERT INTO UserStats (UserId, Mode, TotalScore, RankedScore, PlayCount)
		VALUES (@UserId, @Mode, @TotalScore, @RankedScore, @PlayCount)
		ON CONFLICT(UserId, Mode) DO UPDATE SET
			TotalScore = excluded.TotalScore,
			RankedScore = excluded.RankedScore,
			PlayCount = excluded.PlayCount
		""";

	protected override object WriteParameters(UserStats stats)
	{
		var userId = stats.UserId;
		var mode = (long)stats.Mode;
		var totalScore = stats.TotalScore;
		var rankedScore = stats.RankedScore;
		var playCount = stats.PlayCount;
		return new { UserId = userId, Mode = mode, TotalScore = totalScore, RankedScore = rankedScore, PlayCount = playCount };
	}

	/// <summary>A stored row of the UserStats table.</summary>
	private sealed class StatsRow
	{
		public int UserId { get; set; }
		public long Mode { get; set; }
		public long TotalScore { get; set; }
		public long RankedScore { get; set; }
		public int PlayCount { get; set; }
	}
}
