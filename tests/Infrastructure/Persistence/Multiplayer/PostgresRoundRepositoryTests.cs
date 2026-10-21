using Basil.Application.Persistence.Multiplayer;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Multiplayer;

/// <summary>Contract tests of the storing of the rounds played in matches.</summary>
[Collection("Persistence")]
public sealed class PostgresRoundRepositoryTests : DatabaseTests
{
	private static readonly DateTimeOffset First = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Second = new(2026, 10, 22, 13, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Third = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);

	public PostgresRoundRepositoryTests(PersistenceFixture container) : base(container) { }

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
	public async Task A_round_is_stored()
	{
		var match = await CreateMatchAsync("stored_round_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First), TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select number from rounds where match_id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id },
			rows => rows.Count == 1 && rows[0].Number("number") == 1,
			"the stored round", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task A_round_keeps_the_beatmap_it_is_played_on()
	{
		var match = await CreateMatchAsync("hash_match");
		var hash = StoredScores.Hash("round_beatmap");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First, hash), TestContext.Current.CancellationToken);

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
		await Rounds.CreateOrUpdateAsync(StoredMatches.TeamRound(match, 1, First), TestContext.Current.CancellationToken);

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
		return await Matches.CreateAsync(StoredMatches.Data(name, First), TestContext.Current.CancellationToken);
	}
}
