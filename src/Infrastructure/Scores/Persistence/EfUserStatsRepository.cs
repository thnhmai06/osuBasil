using Basil.Application.Shared;
using Basil.Application.Scores.Persistence;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Scores.Persistence;

/// <summary>Stores users' cumulative score statistics.</summary>
internal sealed class EfUserStatsRepository(DatabaseSession database) : IUserStatsRepository
{
	/// <inheritdoc />
	public async ValueTask<UserStats> GetAsync(UserRecord user, GameMode mode,
		CancellationToken ct = default)
	{
		var userId = user.Id;
		var storedMode = (int)mode;
		var row = await database.ReadAsync((ScoresDbContext context) => context.UserStats
			.Where(stats => stats.UserId == userId && stats.Mode == storedMode)
			.FirstOrDefaultAsync(ct), ct);
		return row is null ? new UserStats { UserId = userId, Mode = mode } : ToStats(row);
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(UserStats stats)
	{
		var row = new UserStatsRow
		{
			UserId = stats.UserId,
			Mode = (int)stats.Mode,
			TotalScore = stats.TotalScore,
			RankedScore = stats.RankedScore,
			PlayCount = stats.PlayCount
		};
		return database.WriteAsync((ScoresDbContext context) =>
				context.UpsertAsync(row, stored => stored.UserId == row.UserId && stored.Mode == row.Mode));
	}

	/// <inheritdoc />
	public async ValueTask<UserStats> AddPlayAsync(UserRecord user, GameMode mode, long totalScore, long rankedScore)
	{
		try
		{
			return await IncrementAsync(user.Id, mode, totalScore, rankedScore);
		}
		catch (AlreadyExistsException)
		{
			// Another first play of the same account and mode created the row between our update and our insert;
			// the row exists now, so the increment finds it.
			return await IncrementAsync(user.Id, mode, totalScore, rankedScore);
		}
	}

	/// <summary>Adds one play with column-level updates, or creates the row when the account has none yet.</summary>
	private Task<UserStats> IncrementAsync(int userId, GameMode mode, long totalScore, long rankedScore)
	{
		return database.WriteAsync((ScoresDbContext context) =>
			AddPlayAsync(context, userId, mode, totalScore, rankedScore));
	}

	/// <summary>Adds one play on a context with column-level updates, or adds the row when the account has none yet.</summary>
	internal static async Task<UserStats> AddPlayAsync(ScoresDbContext context, int userId, GameMode mode,
		long totalScore, long rankedScore)
	{
		var storedMode = (int)mode;
		var updated = await context.UserStats
			.Where(s => s.UserId == userId && s.Mode == storedMode)
			.ExecuteUpdateAsync(set => set
				.SetProperty(s => s.PlayCount, s => s.PlayCount + 1)
				.SetProperty(s => s.TotalScore, s => s.TotalScore + totalScore)
				.SetProperty(s => s.RankedScore, s => s.RankedScore + rankedScore));
		if (updated == 0)
		{
			context.UserStats.Add(new UserStatsRow
			{
				UserId = userId,
				Mode = storedMode,
				TotalScore = totalScore,
				RankedScore = rankedScore,
				PlayCount = 1
			});
			return new UserStats
			{
				UserId = userId, Mode = mode, TotalScore = totalScore, RankedScore = rankedScore, PlayCount = 1
			};
		}

		var row = await context.UserStats.AsNoTracking()
			.FirstAsync(s => s.UserId == userId && s.Mode == storedMode);
		return ToStats(row);
	}

	private static UserStats ToStats(UserStatsRow row)
	{
		return new UserStats
		{
			UserId = row.UserId,
			Mode = (GameMode)row.Mode,
			TotalScore = row.TotalScore,
			RankedScore = row.RankedScore,
			PlayCount = row.PlayCount
		};
	}
}