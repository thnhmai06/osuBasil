using Basil.Application.Persistence.Users;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Postgres;
using Basil.Infrastructure.Persistence.Memory;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Scores;

/// <summary>Stores users' cumulative score statistics.</summary>
internal sealed class PostgresUserStatsRepository(DatabaseReader reader, DatabaseWriter writer)
	: MemoryRepository<(int UserId, GameMode Mode), UserStats>(reader, writer), IUserStatsRepository
{
	/// <inheritdoc />
	public async ValueTask<UserStats> GetAsync(UserRecord user, GameMode mode,
		CancellationToken cancellationToken = default)
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
		var mode = (int)key.Mode;
		var row = await Reader.ReadAsync(context => context.UserStats
			.Where(row => row.UserId == key.UserId && row.Mode == mode)
			.FirstOrDefaultAsync(cancellationToken: CancellationToken.None));
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

	protected override WriteCommand SaveCommand(UserStats stats)
	{
		var userId = stats.UserId;
		var mode = (int)stats.Mode;
		var totalScore = stats.TotalScore;
		var rankedScore = stats.RankedScore;
		var playCount = stats.PlayCount;
		return new WriteCommand(async (context, token) =>
		{
			var updated = await context.UserStats
				.Where(s => s.UserId == userId && s.Mode == mode)
				.ExecuteUpdateAsync(set => set
					.SetProperty(s => s.TotalScore, totalScore)
					.SetProperty(s => s.RankedScore, rankedScore)
					.SetProperty(s => s.PlayCount, playCount), token);
			if (updated == 0)
			{
				context.UserStats.Add(new UserStatsRow
				{
					UserId = userId,
					Mode = mode,
					TotalScore = totalScore,
					RankedScore = rankedScore,
					PlayCount = playCount
				});
				await context.SaveChangesAsync(token);
			}
		});
	}
}