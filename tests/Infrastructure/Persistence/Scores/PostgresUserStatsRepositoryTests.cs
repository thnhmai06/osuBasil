using Basil.Application.Persistence.Users;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Scores;

/// <summary>Contract tests of the storing of the cumulative statistics of an account.</summary>
[Collection("Persistence")]
public sealed class PostgresUserStatsRepositoryTests : DatabaseTests
{
	public PostgresUserStatsRepositoryTests(PersistenceFixture container) : base(container) { }

	private IUserStatsRepository Stats => Get<IUserStatsRepository>();

	[Fact]
	public async Task The_statistics_of_an_account_that_played_nothing_are_zero()
	{
		var player = await CreatePlayerAsync("statless_player");

		var stats = await Stats.GetAsync(player, GameMode.Standard, TestContext.Current.CancellationToken);

		Assert.Equal(0, stats.TotalScore);
		Assert.Equal(0, stats.RankedScore);
		Assert.Equal(0, stats.PlayCount);
	}

	[Fact]
	public async Task The_statistics_of_an_account_that_played_nothing_name_the_account()
	{
		var player = await CreatePlayerAsync("statless_named");

		var stats = await Stats.GetAsync(player, GameMode.Standard, TestContext.Current.CancellationToken);

		Assert.Equal(player.Id, stats.UserId);
		Assert.Equal(GameMode.Standard, stats.Mode);
	}

	[Fact]
	public async Task A_stored_statistic_is_read_back()
	{
		var player = await CreatePlayerAsync("stored_stats");
		await Stats.CreateOrUpdateAsync(new UserStats
		{
			UserId = player.Id,
			Mode = GameMode.Standard,
			TotalScore = 1234,
			RankedScore = 1000,
			PlayCount = 7
		}, TestContext.Current.CancellationToken);

		var stats = await Stats.GetAsync(player, GameMode.Standard, TestContext.Current.CancellationToken);

		Assert.Equal(1234, stats.TotalScore);
		Assert.Equal(1000, stats.RankedScore);
		Assert.Equal(7, stats.PlayCount);
	}

	[Fact]
	public async Task A_stored_statistic_reaches_the_table()
	{
		var player = await CreatePlayerAsync("table_stats");
		await Stats.CreateOrUpdateAsync(new UserStats
		{
			UserId = player.Id,
			Mode = GameMode.Standard,
			TotalScore = 1234,
			RankedScore = 1000,
			PlayCount = 7
		}, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select total_score, ranked_score, play_count from user_stats where user_id = @id and mode = @mode",
			new Dictionary<string, object?> { ["@id"] = player.Id, ["@mode"] = (int)GameMode.Standard },
			rows => rows.Count == 1 && rows[0].Number("total_score") == 1234,
			"the stored statistics", TestContext.Current.CancellationToken);

		Assert.Equal(1000, rows[0].Number("ranked_score"));
		Assert.Equal(7, rows[0].Number("play_count"));
	}

	[Fact]
	public async Task A_stored_statistic_replaces_the_one_stored_before()
	{
		var player = await CreatePlayerAsync("replaced_stats");
		await Stats.CreateOrUpdateAsync(new UserStats
		{
			UserId = player.Id,
			Mode = GameMode.Standard,
			TotalScore = 1234,
			RankedScore = 1000,
			PlayCount = 7
		}, TestContext.Current.CancellationToken);
		await Stats.CreateOrUpdateAsync(new UserStats
		{
			UserId = player.Id,
			Mode = GameMode.Standard,
			TotalScore = 4321,
			RankedScore = 4000,
			PlayCount = 8
		}, TestContext.Current.CancellationToken);

		var stats = await Stats.GetAsync(player, GameMode.Standard, TestContext.Current.CancellationToken);

		Assert.Equal(4321, stats.TotalScore);
		Assert.Equal(8, stats.PlayCount);
	}

	[Fact]
	public async Task The_statistics_of_another_mode_are_kept_apart()
	{
		var player = await CreatePlayerAsync("modes_stats");
		await Stats.CreateOrUpdateAsync(new UserStats
		{
			UserId = player.Id,
			Mode = GameMode.Standard,
			TotalScore = 1234,
			RankedScore = 1000,
			PlayCount = 7
		}, TestContext.Current.CancellationToken);
		await Stats.CreateOrUpdateAsync(new UserStats
		{
			UserId = player.Id,
			Mode = GameMode.Taiko,
			TotalScore = 4321,
			RankedScore = 4000,
			PlayCount = 8
		}, TestContext.Current.CancellationToken);

		var stats = await Stats.GetAsync(player, GameMode.Taiko, TestContext.Current.CancellationToken);

		Assert.Equal(4321, stats.TotalScore);
	}

	[Fact]
	public async Task The_statistics_of_an_account_survive_a_fresh_composition()
	{
		var player = await CreatePlayerAsync("surviving_stats");
		await Stats.CreateOrUpdateAsync(new UserStats
		{
			UserId = player.Id,
			Mode = GameMode.Standard,
			TotalScore = 1234,
			RankedScore = 1000,
			PlayCount = 7
		}, TestContext.Current.CancellationToken);
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

	/// <summary>Creates the account the statistics of the test belong to.</summary>
	/// <param name="name">The name of the account.</param>
	/// <returns>The account.</returns>
	private async Task<UserRecord> CreatePlayerAsync(string name)
	{
		return await Get<IUserRepository>().CreateAsync(new UserData { Name = name }, TestContext.Current.CancellationToken);
	}
}
