using Basil.Application.Storage.Contracts.Scores;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Infrastructure.Storage.Caching;
using Basil.Infrastructure.Storage.Writing;
using Dapper;

namespace Basil.Infrastructure.Storage.Scores;

/// <summary>Stores users' cumulative score statistics.</summary>
internal sealed class PostgresUserStatsRepository(Database database, DatabaseWriter writer)
	: CachedRepository<(int UserId, GameMode Mode), UserStats>(database, writer), IUserStatsRepository
{
	protected override (int UserId, GameMode Mode) KeyOf(UserStats item) => (item.UserId, item.Mode);

	protected override Root RootOf(UserStats item) => Root.User(item.UserId);

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
		_ = SaveAsync(live);
		return Task.CompletedTask;
	}

	protected override async Task<UserStats?> LoadAsync((int UserId, GameMode Mode) key, CancellationToken cancellationToken)
	{
		var row = await Database.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<StatsRow>(
			"select user_id, mode, total_score, ranked_score, play_count from user_stats where user_id = @UserId and mode = @Mode",
			new { UserId = key.UserId, Mode = (int)key.Mode }), cancellationToken);
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
		insert into user_stats (user_id, mode, total_score, ranked_score, play_count)
		values (@UserId, @Mode, @TotalScore, @RankedScore, @PlayCount)
		on conflict (user_id, mode) do update set
			total_score = excluded.total_score,
			ranked_score = excluded.ranked_score,
			play_count = excluded.play_count;
		""";

	protected override object WriteParameters(UserStats stats)
	{
		var userId = stats.UserId;
		var mode = (int)stats.Mode;
		var totalScore = stats.TotalScore;
		var rankedScore = stats.RankedScore;
		var playCount = stats.PlayCount;
		return new { UserId = userId, Mode = mode, TotalScore = totalScore, RankedScore = rankedScore, PlayCount = playCount };
	}

	/// <summary>A stored row of the <c>user_stats</c> table.</summary>
	private sealed class StatsRow
	{
		public int UserId { get; set; }
		public int Mode { get; set; }
		public long TotalScore { get; set; }
		public long RankedScore { get; set; }
		public int PlayCount { get; set; }
	}
}
