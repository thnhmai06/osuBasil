using Basil.Common.Application;
using Basil.Application.Persistence.Scores;
using Basil.Application.Persistence.Users;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Scores;

/// <summary>Contract tests of the storing of submitted scores.</summary>
[Collection("Persistence")]
public sealed class PostgresScoreRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private const int Racers = 8;
	private static readonly DateTimeOffset First = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Second = new(2026, 10, 22, 13, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Third = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);

	private IScoreRepository Scores => Get<IScoreRepository>();

	[Fact]
	public async Task A_created_score_is_given_an_id_by_the_store()
	{
		var player = await CreatePlayerAsync("scored_player");
		var score = await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);

		Assert.True(score.Id >= 1);
	}

	[Fact]
	public async Task Created_scores_are_given_increasing_ids()
	{
		var player = await CreatePlayerAsync("scored_twice");
		var first = await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		var second = await CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 2000, Second);

		Assert.True(second.Id > first.Id);
	}

	[Fact]
	public async Task A_created_score_is_stored()
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
	public async Task A_score_whose_checksum_another_score_has_is_refused()
	{
		var player = await CreatePlayerAsync("checksum_player");
		var checksum = StoredScores.Hash("checksum_one");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First, checksum);

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 2000, Second, checksum));
	}

	[Fact]
	public async Task A_score_whose_checksum_a_stored_score_has_is_refused()
	{
		var player = await CreatePlayerAsync("stored_checksum");
		var checksum = StoredScores.Hash("checksum_two");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First, checksum);

		await using var other = new StorageComposition(ConnectionString);
		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			other.Get<IScoreRepository>().CreateAsync(
				StoredScores.Data(player, StoredScores.Hash("beatmap_two"), 2000, Second, checksum),
				TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_score_is_found_by_its_id()
	{
		var player = await CreatePlayerAsync("found_score");
		var score = await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);

		var found = await Scores.GetAsync(score.Id, TestContext.Current.CancellationToken);

		Assert.NotNull(found);
		Assert.Equal(score.Id, found.Id);
	}

	[Fact]
	public async Task A_score_that_was_never_created_is_not_found()
	{
		Assert.Null(await Scores.GetAsync(987654, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_listing_shows_the_scores_of_one_player()
	{
		var player = await CreatePlayerAsync("listed_player");
		var other = await CreatePlayerAsync("other_player");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		await CreateScoreAsync(other, StoredScores.Hash("beatmap_two"), 2000, Second);

		var page = await Scores.ListAsync(new ScoreQuery(player), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(1000, Assert.Single(page.Items).Value.TotalScore);
	}

	[Fact]
	public async Task A_listing_shows_the_scores_of_one_beatmap()
	{
		var player = await CreatePlayerAsync("hash_player");
		var hash = StoredScores.Hash("listed_beatmap");
		await CreateScoreAsync(player, hash, 1000, First);
		await CreateScoreAsync(player, StoredScores.Hash("other_beatmap"), 2000, Second);

		var page = await Scores.ListAsync(new ScoreQuery(BeatmapHash: hash), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(1000, Assert.Single(page.Items).Value.TotalScore);
	}

	[Fact]
	public async Task A_listing_shows_the_scores_of_one_mod_combination()
	{
		var player = await CreatePlayerAsync("mods_player");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First, mods: GameMods.Hidden);
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 2000, Second);

		var page = await Scores.ListAsync(new ScoreQuery(Mods: GameMods.Hidden), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(1000, Assert.Single(page.Items).Value.TotalScore);
	}

	[Fact]
	public async Task A_listing_shows_the_scores_of_the_named_players()
	{
		var player = await CreatePlayerAsync("named_player");
		var other = await CreatePlayerAsync("other_named");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		await CreateScoreAsync(other, StoredScores.Hash("beatmap_two"), 2000, Second);

		var page = await Scores.ListAsync(new ScoreQuery(Players: new HashSet<UserRecord> { player }),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(1000, Assert.Single(page.Items).Value.TotalScore);
	}

	[Fact]
	public async Task A_best_per_player_listing_keeps_the_highest_score_of_a_player()
	{
		var player = await CreatePlayerAsync("best_player");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 2000, Second);

		var page = await Scores.ListAsync(new ScoreQuery(BestPerPlayer: true), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(2000, Assert.Single(page.Items).Value.TotalScore);
	}

	[Fact]
	public async Task A_best_per_player_listing_keeps_the_earlier_play_of_a_tie()
	{
		var player = await CreatePlayerAsync("tied_player");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 1000, Second);

		var page = await Scores.ListAsync(new ScoreQuery(BestPerPlayer: true), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(First, Assert.Single(page.Items).Value.Timestamp);
	}

	[Fact]
	public async Task A_best_per_player_listing_keeps_the_lower_id_of_a_tie()
	{
		var player = await CreatePlayerAsync("same_moment");
		var first = await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 1000, First);

		var page = await Scores.ListAsync(new ScoreQuery(BestPerPlayer: true), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(first.Id, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task A_beatmap_listing_is_ordered_by_the_total_score()
	{
		var player = await CreatePlayerAsync("ordered_player");
		var hash = StoredScores.Hash("ordered_beatmap");
		await CreateScoreAsync(player, hash, 1000, First);
		await CreateScoreAsync(player, hash, 2000, Second);

		var page = await Scores.ListAsync(new ScoreQuery(BeatmapHash: hash), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 2000, 1000 }, page.Items.Select(score => score.Value.TotalScore));
	}

	[Fact]
	public async Task A_listing_without_a_beatmap_is_ordered_by_the_moment()
	{
		var player = await CreatePlayerAsync("moment_player");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 2000, Third);

		var page = await Scores.ListAsync(new ScoreQuery(), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(new[] { Third, First }, page.Items.Select(score => score.Value.Timestamp));
	}

	[Fact]
	public async Task A_listing_returns_the_requested_part()
	{
		var player = await CreatePlayerAsync("paged_player");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 2000, Second);
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_three"), 3000, Third);

		var page = await Scores.ListAsync(new ScoreQuery(), new PageRequest(1, 1),
			TestContext.Current.CancellationToken);

		Assert.Equal(3, page.Total);
		Assert.Equal(Second, Assert.Single(page.Items).Value.Timestamp);
	}

	[Fact]
	public async Task A_listing_tells_how_many_scores_match()
	{
		var player = await CreatePlayerAsync("counted_player");
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_one"), 1000, First);
		await CreateScoreAsync(player, StoredScores.Hash("beatmap_two"), 2000, Second);

		var page = await Scores.ListAsync(new ScoreQuery(), new PageRequest(0, 1),
			TestContext.Current.CancellationToken);

		Assert.Equal(2, page.Total);
	}

	[Fact]
	public async Task Scores_created_at_once_with_one_checksum_by_one_player_store_exactly_one()
	{
		var player = await CreatePlayerAsync("racing_player");
		var checksum = StoredScores.Hash("racing_checksum");

		var outcomes = await RaceAsync(Enumerable.Repeat(player, Racers).ToList(), checksum);

		await AssertOneStoredAsync(outcomes, checksum);
	}

	[Fact]
	public async Task Scores_created_at_once_with_one_checksum_by_different_players_store_exactly_one()
	{
		var players = new List<UserRecord>();
		for (var i = 0; i < Racers; i++)
			players.Add(await CreatePlayerAsync($"racing_player_{i}"));
		var checksum = StoredScores.Hash("racing_checksum_players");

		var outcomes = await RaceAsync(players, checksum);

		await AssertOneStoredAsync(outcomes, checksum);
	}

	[Fact]
	public async Task Scores_created_at_once_with_one_checksum_leave_the_winner_findable()
	{
		var player = await CreatePlayerAsync("racing_found");
		var checksum = StoredScores.Hash("racing_checksum_found");

		var outcomes = await RaceAsync(Enumerable.Repeat(player, Racers).ToList(), checksum);

		var winner = Assert.Single(outcomes.OfType<ScoreRecord>());
		Assert.Same(winner, await Scores.GetAsync(winner.Id, TestContext.Current.CancellationToken));
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
						checksum),
					TestContext.Current.CancellationToken);
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
			.CreateAsync(new UserData { Name = name }, TestContext.Current.CancellationToken);
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
		return await Scores.CreateAsync(StoredScores.Data(player, beatmapHash, totalScore, timestamp, checksum, mods),
			TestContext.Current.CancellationToken);
	}
}