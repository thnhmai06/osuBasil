using Basil.Application.Scores.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Scores.Tests.Persistence;

/// <summary>Contract tests of the storing of the cumulative statistics of an account.</summary>
[Collection("Persistence")]
public sealed class EfUserStatsRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private IUserStatsRepository Stats => Get<IUserStatsRepository>();

	[Fact]
	public async Task UnplayedAccount_StatisticsZero()
	{
		var player = await CreatePlayerAsync("statless_player");

		var stats = await Stats.GetAsync(player, GameMode.Standard, TestContext.Current.CancellationToken);

		Assert.Equal(0, stats.TotalScore);
		Assert.Equal(0, stats.RankedScore);
		Assert.Equal(0, stats.PlayCount);
	}

	[Fact]
	public async Task UnplayedAccount_StatisticsNameAccount()
	{
		var player = await CreatePlayerAsync("statless_named");

		var stats = await Stats.GetAsync(player, GameMode.Standard, TestContext.Current.CancellationToken);

		Assert.Equal(player.Id, stats.UserId);
		Assert.Equal(GameMode.Standard, stats.Mode);
	}

	[Fact]
	public async Task StoredStatistic_ReadBack()
	{
		var player = await CreatePlayerAsync("stored_stats");
		await Stats.CreateOrUpdateAsync(new UserStats
		{
			UserId = player.Id,
			Mode = GameMode.Standard,
			TotalScore = 1234,
			RankedScore = 1000,
			PlayCount = 7
		});

		var stats = await Stats.GetAsync(player, GameMode.Standard, TestContext.Current.CancellationToken);

		Assert.Equal(1234, stats.TotalScore);
		Assert.Equal(1000, stats.RankedScore);
		Assert.Equal(7, stats.PlayCount);
	}

	[Fact]
	public async Task StoredStatistic_ReachesTable()
	{
		var player = await CreatePlayerAsync("table_stats");
		await Stats.CreateOrUpdateAsync(new UserStats
		{
			UserId = player.Id,
			Mode = GameMode.Standard,
			TotalScore = 1234,
			RankedScore = 1000,
			PlayCount = 7
		});

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select total_score, ranked_score, play_count from user_stats where user_id = @id and mode = @mode",
			new Dictionary<string, object?> { ["@id"] = player.Id, ["@mode"] = (int)GameMode.Standard },
			rows => rows.Count == 1 && rows[0].Number("total_score") == 1234,
			"the stored statistics", TestContext.Current.CancellationToken);

		Assert.Equal(1000, rows[0].Number("ranked_score"));
		Assert.Equal(7, rows[0].Number("play_count"));
	}

	[Fact]
	public async Task StoredStatistic_ReplacesPrevious()
	{
		var player = await CreatePlayerAsync("replaced_stats");
		await Stats.CreateOrUpdateAsync(new UserStats
		{
			UserId = player.Id,
			Mode = GameMode.Standard,
			TotalScore = 1234,
			RankedScore = 1000,
			PlayCount = 7
		});
		await Stats.CreateOrUpdateAsync(new UserStats
		{
			UserId = player.Id,
			Mode = GameMode.Standard,
			TotalScore = 4321,
			RankedScore = 4000,
			PlayCount = 8
		});

		var stats = await Stats.GetAsync(player, GameMode.Standard, TestContext.Current.CancellationToken);

		Assert.Equal(4321, stats.TotalScore);
		Assert.Equal(8, stats.PlayCount);
	}

	[Fact]
	public async Task OtherModeStatistics_KeptApart()
	{
		var player = await CreatePlayerAsync("modes_stats");
		await Stats.CreateOrUpdateAsync(new UserStats
		{
			UserId = player.Id,
			Mode = GameMode.Standard,
			TotalScore = 1234,
			RankedScore = 1000,
			PlayCount = 7
		});
		await Stats.CreateOrUpdateAsync(new UserStats
		{
			UserId = player.Id,
			Mode = GameMode.Taiko,
			TotalScore = 4321,
			RankedScore = 4000,
			PlayCount = 8
		});

		var stats = await Stats.GetAsync(player, GameMode.Taiko, TestContext.Current.CancellationToken);

		Assert.Equal(4321, stats.TotalScore);
	}

	[Fact]
	public async Task AccountStatistics_SurviveRecomposition()
	{
		var player = await CreatePlayerAsync("surviving_stats");
		await Stats.CreateOrUpdateAsync(new UserStats
		{
			UserId = player.Id,
			Mode = GameMode.Standard,
			TotalScore = 1234,
			RankedScore = 1000,
			PlayCount = 7
		});
		await StoredRows.WaitForAsync(ConnectionString, "select total_score from user_stats where user_id = @id",
			new Dictionary<string, object?> { ["@id"] = player.Id },
			rows => rows.Count == 1 && rows[0].Number("total_score") == 1234,
			"the stored statistics", TestContext.Current.CancellationToken);

		await using var other = new StorageComposition(ConnectionString);
		var stats = await other.Get<IUserStatsRepository>().GetAsync(player, GameMode.Standard,
			TestContext.Current.CancellationToken);

		Assert.Equal(1234, stats.TotalScore);
		Assert.Equal(7, stats.PlayCount);
	}

	[Fact]
	public async Task PlayAdded_CountedInStatistics()
	{
		var player = await CreatePlayerAsync("added_play");

		await Stats.AddPlayAsync(player, GameMode.Standard, 300, 200);
		var stats = await Stats.AddPlayAsync(player, GameMode.Standard, 50, 0);

		Assert.Equal(2, stats.PlayCount);
		Assert.Equal(350, stats.TotalScore);
		Assert.Equal(200, stats.RankedScore);
	}

	[Fact]
	public async Task ConcurrentPlays_AllCounted()
	{
		const int plays = 400;
		var player = await CreatePlayerAsync("racing_stats");
		await Stats.GetAsync(player, GameMode.Standard, TestContext.Current.CancellationToken);
		await Parallel.ForEachAsync(Enumerable.Range(1, plays),
			new ParallelOptions
				{ MaxDegreeOfParallelism = 16, CancellationToken = TestContext.Current.CancellationToken },
			async (score, token) => await Stats.AddPlayAsync(player, GameMode.Standard, score, score * 10L));

		var stats = await Stats.GetAsync(player, GameMode.Standard, TestContext.Current.CancellationToken);
		Assert.Equal(plays, stats.PlayCount);
		Assert.Equal(plays * (plays + 1) / 2, stats.TotalScore);
		Assert.Equal(plays * (plays + 1) / 2 * 10, stats.RankedScore);
		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select total_score, ranked_score, play_count from user_stats where user_id = @id and mode = @mode",
			new Dictionary<string, object?> { ["@id"] = player.Id, ["@mode"] = (int)GameMode.Standard },
			rows => rows.Count == 1 && rows[0].Number("play_count") == plays,
			"the stored statistics", TestContext.Current.CancellationToken);
		Assert.Equal(plays * (plays + 1) / 2, rows[0].Number("total_score"));
		Assert.Equal(plays * (plays + 1) / 2 * 10, rows[0].Number("ranked_score"));
	}

	/// <summary>Creates the account the statistics of the test belong to.</summary>
	/// <param name="name">The name of the account.</param>
	/// <returns>The account.</returns>
	private async Task<UserRecord> CreatePlayerAsync(string name)
	{
		return await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = name });
	}
}