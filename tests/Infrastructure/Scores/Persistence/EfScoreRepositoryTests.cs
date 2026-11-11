using Basil.Application.Shared;
using Basil.Application.Scores.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Shared;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Scores.Tests.Persistence;

/// <summary>Contract tests of the storing of submitted scores.</summary>
[Collection("Persistence")]
public sealed class EfScoreRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private const int Racers = 8;
	private static readonly DateTimeOffset First = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Second = new(2026, 10, 22, 13, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Third = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);

	private IScoreRepository Scores => Get<IScoreRepository>();

	[Fact]
	public async Task CreatedScore_GetsStoreId()
	{
		var player = await CreatePlayerAsync("scored_player");
		var score = await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);

		Assert.True(score.Id >= 1);
	}

	[Fact]
	public async Task CreatedScores_GetIncreasingIds()
	{
		var player = await CreatePlayerAsync("scored_twice");
		var first = await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		var second = await CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 2000, Second);

		Assert.True(second.Id > first.Id);
	}

	[Fact]
	public async Task CreatedScore_Stored()
	{
		var player = await CreatePlayerAsync("stored_score");
		var score = await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1234, First);

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select user_id, total_score from scores where id = @id",
			new Dictionary<string, object?> { ["@id"] = score.Id },
			rows => rows.Count == 1 && rows[0].Number("total_score") == 1234,
			"the stored score", TestContext.Current.CancellationToken);

		Assert.Equal(player.Id, rows[0].Number("user_id"));
	}

	[Fact]
	public async Task ScoreAndPlay_StoredTogether()
	{
		var player = await CreatePlayerAsync("played_once");

		var (score, stats) = await Scores.CreateWithPlayAsync(
			StoredScores.Data(player, StoredScores.Hash("beatmap_one"), 1000, First), 700);

		Assert.NotNull(await Scores.GetAsync(score.Id, TestContext.Current.CancellationToken));
		Assert.Equal((1, 1000, 700), (stats.PlayCount, stats.TotalScore, stats.RankedScore));
	}

	[Fact]
	public async Task PlayNotCounted_ScoreNotStored()
	{
		var player = await CreatePlayerAsync("at_the_limit");
		var data = StoredScores.Data(player, StoredScores.Hash("beatmap_one"), 1000, First);
		await Get<IUserStatsRepository>().CreateOrUpdateAsync(
			new UserStats { UserId = player.Id, Mode = data.Mode, TotalScore = long.MaxValue });

		await Assert.ThrowsAnyAsync<Exception>(() =>
			Scores.CreateWithPlayAsync(data, 0));

		var stored = await Scores.ListAsync(new ScoreQuery(player), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);
		Assert.Empty(stored.Items);
	}

	[Fact]
	public async Task ScoresById_AscendingSkippingUnknown()
	{
		var player = await CreatePlayerAsync("got_many");
		var first = await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		var second = await CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 2000, Second);

		var got = await Scores.GetManyAsync([second.Id, 99_999_999, first.Id], TestContext.Current.CancellationToken);

		Assert.Equal(new[] { first.Id, second.Id }, got.Select(score => score.Id));
	}

	[Fact]
	public async Task ManyScores_RespectsCeiling()
	{
		var player = await CreatePlayerAsync("got_ceiling");
		var original = await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		var rows = await StoredRows.ReadAsync(ConnectionString, "select * from scores where id = @id",
			new Dictionary<string, object?> { ["@id"] = original.Id }, TestContext.Current.CancellationToken);
		var ids = Enumerable.Range(1, ListLimits.Ceiling + 1).Select(g => 100_000_000 + g).ToArray();
		await StoredRows.InsertAsync(ConnectionString, "scores", ids.Select(id =>
		{
			var copy = new Dictionary<string, object?>(rows[0].Values);
			copy["id"] = id;
			copy["checksum"] = id.ToString("x32");
			return (IReadOnlyDictionary<string, object?>)copy;
		}), TestContext.Current.CancellationToken);

		var got = await Scores.GetManyAsync(ids, TestContext.Current.CancellationToken);

		Assert.Equal(ListLimits.Ceiling, got.Count);
	}

	[Fact]
	public async Task DuplicateChecksumScore_Refused()
	{
		var player = await CreatePlayerAsync("checksum_player");
		var checksum = StoredScores.Hash("checksum_one");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First, checksum);

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 2000, Second, checksum));
	}

	[Fact]
	public async Task StoredChecksumScore_Refused()
	{
		var player = await CreatePlayerAsync("stored_checksum");
		var checksum = StoredScores.Hash("checksum_two");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First, checksum);

		await using var other = new StorageComposition(ConnectionString);
		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			other.Get<IScoreRepository>().CreateAsync(
				StoredScores.Data(player, StoredScores.Hash("beatmap_two"), 2000, Second, checksum)));
	}

	[Fact]
	public async Task ScoreById_Found()
	{
		var player = await CreatePlayerAsync("found_score");
		var score = await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);

		var found = await Scores.GetAsync(score.Id, TestContext.Current.CancellationToken);

		Assert.NotNull(found);
		Assert.Equal(score.Id, found.Id);
	}

	[Fact]
	public async Task UncreatedScore_NotFound()
	{
		Assert.Null(await Scores.GetAsync(987654, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Listing_OnePlayerScores()
	{
		var player = await CreatePlayerAsync("listed_player");
		var other = await CreatePlayerAsync("other_player");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		await CreateScoreAsync(other, StoredScores.Hash("beatmap_two"), 2000, Second);

		var page = await Scores.ListAsync(new ScoreQuery(player), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(1000, Assert.Single(page.Items).Value.LastState.TotalScore);
	}

	[Fact]
	public async Task Listing_OneBeatmapScores()
	{
		var player = await CreatePlayerAsync("hash_player");
		var hash = StoredScores.Hash("listed_beatmap");
		await CreateScoreAsync(player, hash, 1000, First);
		await CreateScoreAsync(player, StoredScores.Hash("other_beatmap"), 2000, Second);

		var page = await Scores.ListAsync(new ScoreQuery(BeatmapHash: hash), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(1000, Assert.Single(page.Items).Value.LastState.TotalScore);
	}

	[Fact]
	public async Task Listing_OneModScores()
	{
		var player = await CreatePlayerAsync("mods_player");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First, mods: GameMods.Hidden);
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 2000, Second);

		var page = await Scores.ListAsync(new ScoreQuery(Mods: GameMods.Hidden), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(1000, Assert.Single(page.Items).Value.LastState.TotalScore);
	}

	[Fact]
	public async Task Listing_NamedPlayerScores()
	{
		var player = await CreatePlayerAsync("named_player");
		var other = await CreatePlayerAsync("other_named");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		await CreateScoreAsync(other, StoredScores.Hash("beatmap_two"), 2000, Second);

		var page = await Scores.ListAsync(new ScoreQuery(Players: new HashSet<UserRecord> { player }),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(1000, Assert.Single(page.Items).Value.LastState.TotalScore);
	}

	[Fact]
	public async Task BestPerPlayer_KeepsHighestScore()
	{
		var player = await CreatePlayerAsync("best_player");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 2000, Second);

		var page = await Scores.ListAsync(new ScoreQuery(BestPerPlayer: true), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(2000, Assert.Single(page.Items).Value.LastState.TotalScore);
	}

	[Fact]
	public async Task BestPerPlayer_KeepsEarlierPlay()
	{
		var player = await CreatePlayerAsync("tied_player");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 1000, Second);

		var page = await Scores.ListAsync(new ScoreQuery(BestPerPlayer: true), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(First, Assert.Single(page.Items).Value.Timestamp);
	}

	[Fact]
	public async Task BestPerPlayer_KeepsLowerId()
	{
		var player = await CreatePlayerAsync("same_moment");
		var first = await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 1000, First);

		var page = await Scores.ListAsync(new ScoreQuery(BestPerPlayer: true), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(first.Id, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task BeatmapListing_ByTotalScore()
	{
		var player = await CreatePlayerAsync("ordered_player");
		var hash = StoredScores.Hash("ordered_beatmap");
		await CreateScoreAsync(player, hash, 1000, First);
		await CreateScoreAsync(player, hash, 2000, Second);

		var page = await Scores.ListAsync(new ScoreQuery(BeatmapHash: hash), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 2000, 1000 }, page.Items.Select(score => score.Value.LastState.TotalScore));
	}

	[Fact]
	public async Task NoBeatmapListing_OrderedByMoment()
	{
		var player = await CreatePlayerAsync("moment_player");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 2000, Third);

		var page = await Scores.ListAsync(new ScoreQuery(), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(new[] { Third, First }, page.Items.Select(score => score.Value.Timestamp));
	}

	[Fact]
	public async Task Listing_ReturnsRequestedPage()
	{
		var player = await CreatePlayerAsync("paged_player");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 2000, Second);
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_three"), 3000, Third);

		var page = await Scores.ListAsync(new ScoreQuery(), new PageRequest(1, 1),
			TestContext.Current.CancellationToken);

		Assert.True(page.HasNext);
		Assert.Equal(Second, Assert.Single(page.Items).Value.Timestamp);
	}

	[Fact]
	public async Task Listing_ReportsMoreScores()
	{
		var player = await CreatePlayerAsync("counted_player");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 2000, Second);

		var partial = await Scores.ListAsync(new ScoreQuery(), new PageRequest(0, 1),
			TestContext.Current.CancellationToken);
		var exact = await Scores.ListAsync(new ScoreQuery(), new PageRequest(0, 2),
			TestContext.Current.CancellationToken);

		Assert.Equal((true, 1, false, 2), (partial.HasNext, partial.Items.Count, exact.HasNext, exact.Items.Count));
	}

	[Fact]
	public async Task RepeatedPlayerChecksum_StoresOne()
	{
		var player = await CreatePlayerAsync("racing_player");
		var checksum = StoredScores.Hash("racing_checksum");

		var outcomes = await RaceAsync(Enumerable.Repeat(player, Racers).ToList(), checksum);

		await AssertOneStoredAsync(outcomes, checksum);
	}

	[Fact]
	public async Task PlayersShareChecksum_StoresOne()
	{
		var players = new List<UserRecord>();
		for (var i = 0; i < Racers; i++)
			players.Add(await CreatePlayerAsync($"racing_player_{i}"));
		var checksum = StoredScores.Hash("racing_checksum_players");

		var outcomes = await RaceAsync(players, checksum);

		await AssertOneStoredAsync(outcomes, checksum);
	}

	[Fact]
	public async Task ConcurrentSameChecksum_WinnerFindable()
	{
		var player = await CreatePlayerAsync("racing_found");
		var checksum = StoredScores.Hash("racing_checksum_found");

		var outcomes = await RaceAsync(Enumerable.Repeat(player, Racers).ToList(), checksum);

		var winner = Assert.Single(outcomes.OfType<ScoreRecord>());
		Assert.Equal(winner, await Scores.GetAsync(winner.Id, TestContext.Current.CancellationToken));
	}

	/// <summary>Creates a play for every player at the same moment, all with the same checksum.</summary>
	/// <param name="players">The players, one creation each.</param>
	/// <param name="checksum">The checksum every play carries.</param>
	/// <returns>The stored score or the exception of every creation.</returns>
	private async Task<List<object>> RaceAsync(IReadOnlyList<UserRecord> players, Md5 checksum)
	{
		var start = new TaskCompletionSource();
		var attempts = players.Select(async (player, index) =>
		{
			await start.Task;
			try
			{
				return (object)await Scores.CreateAsync(
					StoredScores.Data(player, StoredScores.Hash($"racing_beatmap_{index}"), 1000 + index, First,
						checksum));
			}
			catch (Exception exception)
			{
				return exception;
			}
		}).ToList();
		start.SetResult();
		return [.. await Task.WhenAll(attempts)];
	}

	private async Task AssertOneStoredAsync(List<object> outcomes, Md5 checksum)
	{
		Assert.Single(outcomes.OfType<ScoreRecord>());
		Assert.All(outcomes.OfType<Exception>(), exception => Assert.IsType<AlreadyExistsException>(exception));
		Assert.Equal(Racers - 1, outcomes.OfType<Exception>().Count());
		var rows = await StoredRows.ReadAsync(ConnectionString, "select count(*) as n from scores where checksum = @c",
			new Dictionary<string, object?> { ["@c"] = checksum.HashValue }, TestContext.Current.CancellationToken);
		Assert.Equal(1, Assert.Single(rows).Number("n"));
	}

	/// <summary>Creates the account that played.</summary>
	/// <param name="name">The name of the account.</param>
	/// <returns>The account.</returns>
	private async Task<UserRecord> CreatePlayerAsync(string name)
	{
		return await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = name });
	}

	/// <summary>Creates a play of the given account.</summary>
	/// <param name="player">The account that played.</param>
	/// <param name="beatmapHash">The hash of the beatmap played.</param>
	/// <param name="totalScore">The total score the play achieved.</param>
	/// <param name="timestamp">The moment the play happened.</param>
	/// <param name="checksum">The checksum the client computed for the submission.</param>
	/// <param name="mods">The mods applied to the play.</param>
	/// <returns>The play.</returns>
	private async Task<ScoreRecord> CreateScoreAsync(UserRecord player, Md5 beatmapHash, int totalScore,
		DateTimeOffset timestamp, Md5? checksum = null, GameMods mods = GameMods.NoMod)
	{
		return await Scores.CreateAsync(StoredScores.Data(player, beatmapHash, totalScore, timestamp, checksum, mods));
	}
}