using Basil.Application.Persistence.Multiplayer;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Multiplayer;

/// <summary>Contract tests of the storing of the rounds played in matches.</summary>
[Collection("Persistence")]
public sealed class PostgresRoundRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset First = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Second = new(2026, 10, 22, 13, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Third = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);

	private IMatchRepository Matches => Get<IMatchRepository>();

	private IRoundRepository Rounds => Get<IRoundRepository>();

	[Fact]
	public async Task A_stored_round_is_listed_under_its_match()
	{
		var match = await CreateMatchAsync("rounds_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First), TestContext.Current.CancellationToken);

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(1, Assert.Single(listed).Index);
	}

	[Fact]
	public async Task A_round_just_given_to_the_repository_is_listed_at_once()
	{
		var match = await CreateMatchAsync("pending_round_match");
		var round = StoredMatches.Round(match, 1, First);

		await Rounds.CreateOrUpdateAsync(round, TestContext.Current.CancellationToken);
		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Same(round, Assert.Single(listed));
	}

	[Fact]
	public async Task The_rounds_of_a_match_are_listed_in_the_order_they_were_played()
	{
		var match = await CreateMatchAsync("ordered_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 2, Second), TestContext.Current.CancellationToken);
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 3, Third), TestContext.Current.CancellationToken);
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First), TestContext.Current.CancellationToken);

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 1, 2, 3 }, listed.Select(round => round.Index));
	}

	[Fact]
	public async Task A_round_is_stored_after_it_was_given_to_the_repository()
	{
		var match = await CreateMatchAsync("stored_round_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First), TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select number from rounds where match_id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id }, rows => rows.Count == 1, "the round",
			TestContext.Current.CancellationToken);

		Assert.Equal(1, Assert.Single(rows).Number("number"));
	}

	[Fact]
	public async Task The_end_of_a_round_is_stored_after_it_was_given_to_the_repository()
	{
		var match = await CreateMatchAsync("ended_round_match");
		var round = StoredMatches.Round(match, 1, First);
		await Rounds.CreateOrUpdateAsync(round, TestContext.Current.CancellationToken);
		round.EndedAt = Second;
		await Rounds.CreateOrUpdateAsync(round, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select ended_at from rounds where match_id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id },
			rows => rows.Count == 1 && rows[0].Instant("ended_at") == Second, "the end of the round",
			TestContext.Current.CancellationToken);

		Assert.Equal(Second, Assert.Single(rows).Instant("ended_at"));
	}

	[Fact]
	public async Task The_abortion_of_a_round_is_stored_after_it_was_given_to_the_repository()
	{
		var match = await CreateMatchAsync("aborted_round_match");
		var round = StoredMatches.Round(match, 1, First);
		await Rounds.CreateOrUpdateAsync(round, TestContext.Current.CancellationToken);
		round.EndedAt = Second;
		round.Aborted = true;
		await Rounds.CreateOrUpdateAsync(round, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select aborted from rounds where match_id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id },
			rows => rows.Count == 1 && rows[0].Bool("aborted"), "the abortion of the round",
			TestContext.Current.CancellationToken);

		Assert.True(Assert.Single(rows).Bool("aborted"));
	}

	[Fact]
	public async Task The_decision_of_a_round_is_stored_and_read_by_a_server_that_starts_afterwards()
	{
		var match = await CreateMatchAsync("decision_round_match");
		var round = StoredMatches.Round(match, 1, First);
		round.Decision = new RoundVerdict([7, 3], 42, null, 1500.5, false, null, null);
		await Rounds.CreateOrUpdateAsync(round, TestContext.Current.CancellationToken);
		await StoredRows.WaitForAsync(ConnectionString, "select result from rounds where match_id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id },
			rows => rows.Count == 1 && rows[0]["result"] is not null,
			"the decision", TestContext.Current.CancellationToken);

		await using var other = new StorageComposition(ConnectionString);
		var decision = Assert.Single(await other.Get<IRoundRepository>().ListAsync(match,
			TestContext.Current.CancellationToken)).Decision;

		Assert.NotNull(decision);
		Assert.Equal([7, 3], decision.ScoreOrder);
		Assert.Equal((42, 1500.5, false), (decision.WinnerId, decision.Margin, decision.IsDraw));
	}

	[Fact]
	public async Task A_server_that_starts_afterwards_lists_the_stored_rounds_of_a_match()
	{
		var match = await CreateMatchAsync("restart_round_match");
		var round = StoredMatches.Round(match, 1, First);
		await Rounds.CreateOrUpdateAsync(round, TestContext.Current.CancellationToken);
		round.EndedAt = Second;
		round.Aborted = true;
		await Rounds.CreateOrUpdateAsync(round, TestContext.Current.CancellationToken);
		await StoredRows.WaitForAsync(ConnectionString, "select aborted from rounds where match_id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id },
			rows => rows.Count == 1 && rows[0].Bool("aborted"), "the abortion of the round",
			TestContext.Current.CancellationToken);

		await using var other = new StorageComposition(ConnectionString);
		var listed = Assert.Single(await other.Get<IRoundRepository>().ListAsync(match,
			TestContext.Current.CancellationToken));

		Assert.Equal((1, Second, true), (listed.Index, listed.EndedAt, listed.Aborted));
	}

	[Fact]
	public async Task A_round_in_use_is_listed_as_the_same_instance()
	{
		var match = await CreateMatchAsync("held_round_match");
		var round = StoredMatches.Round(match, 1, First);
		await Rounds.CreateOrUpdateAsync(round, TestContext.Current.CancellationToken);

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Same(round, Assert.Single(listed));
		GC.KeepAlive(round);
	}

	[Fact]
	public async Task A_round_in_use_takes_the_end_stored_for_another_instance_of_it()
	{
		var match = await CreateMatchAsync("held_end_match");
		var round = StoredMatches.Round(match, 1, First);
		await Rounds.CreateOrUpdateAsync(round, TestContext.Current.CancellationToken);
		var ended = StoredMatches.Round(match, 1, First);
		ended.EndedAt = Second;
		await Rounds.CreateOrUpdateAsync(ended, TestContext.Current.CancellationToken);

		var listed = Assert.Single(await Rounds.ListAsync(match, TestContext.Current.CancellationToken));

		Assert.Equal(Second, listed.EndedAt);
		GC.KeepAlive(round);
	}

	[Fact]
	public async Task A_round_keeps_the_beatmap_it_is_played_on()
	{
		var match = await CreateMatchAsync("hash_match");
		var hash = StoredScores.Hash("round_beatmap");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First, hash),
			TestContext.Current.CancellationToken);

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(hash, Assert.Single(listed).BeatmapHash);
	}

	[Fact]
	public async Task A_round_keeps_the_mode_it_is_played_in()
	{
		var match = await CreateMatchAsync("mode_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First, settings: new MatchSettings
		{
			Mode = GameMode.Taiko
		}), TestContext.Current.CancellationToken);

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(GameMode.Taiko, Assert.Single(listed).Settings.Mode);
	}

	[Fact]
	public async Task A_round_keeps_the_mods_it_is_played_with()
	{
		var match = await CreateMatchAsync("mods_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First, settings: new MatchSettings
		{
			Mods = GameMods.Hidden
		}), TestContext.Current.CancellationToken);

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(GameMods.Hidden, Assert.Single(listed).Settings.Mods);
	}

	[Fact]
	public async Task A_round_keeps_the_team_arrangement_it_is_played_with()
	{
		var match = await CreateMatchAsync("team_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.TeamRound(match, 1, First),
			TestContext.Current.CancellationToken);

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(GameTeamType.TeamVs, Assert.Single(listed).Settings.TeamType);
	}

	[Fact]
	public async Task A_round_keeps_the_match_it_belongs_to()
	{
		var match = await CreateMatchAsync("owning_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First), TestContext.Current.CancellationToken);

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(match.Id, Assert.Single(listed).Match.Id);
	}

	[Fact]
	public async Task A_round_with_the_number_of_a_stored_round_replaces_it()
	{
		var match = await CreateMatchAsync("replaced_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First), TestContext.Current.CancellationToken);
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, Second), TestContext.Current.CancellationToken);

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Single(listed);
	}

	[Fact]
	public async Task The_rounds_of_another_match_are_left_out()
	{
		var match = await CreateMatchAsync("own_match");
		var other = await CreateMatchAsync("other_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First), TestContext.Current.CancellationToken);
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(other, 1, First), TestContext.Current.CancellationToken);

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Single(listed);
	}

	[Fact]
	public async Task A_match_without_a_round_lists_none()
	{
		var match = await CreateMatchAsync("empty_match");

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Empty(listed);
	}

	/// <summary>Creates a match the rounds of the test belong to.</summary>
	/// <param name="name">The name of the match.</param>
	/// <returns>The match.</returns>
	private async Task<MatchRecord> CreateMatchAsync(string name)
	{
		return await CreateStoredMatchAsync(name, First);
	}
}