using Basil.Application.Storage.Contracts.Scores;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Infrastructure.Storage.Common.Database;
using Basil.Infrastructure.Storage.Common.Writing;
using Basil.Infrastructure.Storage.Memory;
using Dapper;

namespace Basil.Infrastructure.Storage.Scores;

/// <summary>Stores users' cumulative score statistics.</summary>
internal sealed class PostgresUserStatsRepository(DatabaseReader reader, DatabaseWriter writer)
	: MemoryRepository<(int UserId, GameMode Mode), UserStats>(reader, writer), IUserStatsRepository
{
	protected override string WriteSql =>
		"""
		insert into user_stats (user_id, mode, total_score, ranked_score, play_count)
		values (@UserId, @Mode, @TotalScore, @RankedScore, @PlayCount)
		on conflict (user_id, mode) do update set
			total_score = excluded.total_score,
			ranked_score = excluded.ranked_score,
			play_count = excluded.play_count;
		""";

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
		_ = SaveAsync(stats);
		return Task.CompletedTask;
	}

	protected override (int UserId, GameMode Mode) KeyOf(UserStats item)
	{
		return (item.UserId, item.Mode);
	}

	protected override Root RootOf(UserStats item)
	{
		return Root.User(item.UserId);
	}

	protected override void CopyTo(UserStats live, UserStats from)
	{
		live.TotalScore = from.TotalScore;
		live.RankedScore = from.RankedScore;
		live.PlayCount = from.PlayCount;
	}

	protected override async Task<UserStats?> LoadAsync((int UserId, GameMode Mode) key)
	{
		var row = await Reader.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<StatsRow>(
			"select user_id, mode, total_score, ranked_score, play_count from user_stats where user_id = @UserId and mode = @Mode",
			new { key.UserId, Mode = (int)key.Mode }));
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

	protected override object WriteParameters(UserStats stats)
	{
		var userId = stats.UserId;
		var mode = (int)stats.Mode;
		var totalScore = stats.TotalScore;
		var rankedScore = stats.RankedScore;
		var playCount = stats.PlayCount;
		return new
		{
			UserId = userId, Mode = mode, TotalScore = totalScore, RankedScore = rankedScore, PlayCount = playCount
		};
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