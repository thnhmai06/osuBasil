using Basil.Application.Shared;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Multiplayer.Tests.Persistence;

/// <summary>Contract tests of the storing of the rounds played in matches.</summary>
[Collection("Persistence")]
public sealed class EfRoundRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset First = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Second = new(2026, 10, 22, 13, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Third = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);

	private IMatchRepository Matches => Get<IMatchRepository>();

	private IRoundRepository Rounds => Get<IRoundRepository>();

	[Fact]
	public async Task Listing_RespectsRoundCeiling()
	{
		var match = await CreateMatchAsync("long_round_match");
		await StoredRows.InsertAsync(ConnectionString, "rounds", Enumerable.Range(1, ListLimits.Ceiling + 1).Select(g =>
			(IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
			{
				["match_id"] = match.Id, ["number"] = g, ["beatmap_hash"] = StoredMatches.BeatmapHash.HashValue,
				["mode"] = 0, ["mods"] = 0, ["freemods"] = false, ["team_type"] = 0, ["win_condition"] = 0,
				["seed"] = 0, ["started_at"] = First, ["aborted"] = false
			}), TestContext.Current.CancellationToken);

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(ListLimits.Ceiling, listed.Count);
	}

	[Fact]
	public async Task StoredRound_ListedUnderMatch()
	{
		var match = await CreateMatchAsync("rounds_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First));

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(1, Assert.Single(listed).Index);
	}

	[Fact]
	public async Task JustGivenRound_ListedAtOnce()
	{
		var match = await CreateMatchAsync("pending_round_match");
		var round = StoredMatches.Round(match, 1, First);

		await Rounds.CreateOrUpdateAsync(round);
		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(1, Assert.Single(listed).Index);
	}

	[Fact]
	public async Task MatchRounds_ListedInOrder()
	{
		var match = await CreateMatchAsync("ordered_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 2, Second));
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 3, Third));
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First));

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 1, 2, 3 }, listed.Select(round => round.Index));
	}

	[Fact]
	public async Task GivenRound_EventuallyStored()
	{
		var match = await CreateMatchAsync("stored_round_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First));

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select number from rounds where match_id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id }, rows => rows.Count == 1, "the round",
			TestContext.Current.CancellationToken);

		Assert.Equal(1, Assert.Single(rows).Number("number"));
	}

	[Fact]
	public async Task GivenRoundEnd_Stored()
	{
		var match = await CreateMatchAsync("ended_round_match");
		var round = StoredMatches.Round(match, 1, First);
		await Rounds.CreateOrUpdateAsync(round);
		round.EndedAt = Second;
		await Rounds.CreateOrUpdateAsync(round);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select ended_at from rounds where match_id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id },
			rows => rows.Count == 1 && rows[0].Values["ended_at"] is not null && rows[0].Instant("ended_at") == Second,
			"the end of the round",
			TestContext.Current.CancellationToken);

		Assert.Equal(Second, Assert.Single(rows).Instant("ended_at"));
	}

	[Fact]
	public async Task GivenRoundAbortion_Stored()
	{
		var match = await CreateMatchAsync("aborted_round_match");
		var round = StoredMatches.Round(match, 1, First);
		await Rounds.CreateOrUpdateAsync(round);
		round.EndedAt = Second;
		round.Aborted = true;
		await Rounds.CreateOrUpdateAsync(round);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select aborted from rounds where match_id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id },
			rows => rows.Count == 1 && rows[0].Bool("aborted"), "the abortion of the round",
			TestContext.Current.CancellationToken);

		Assert.True(Assert.Single(rows).Bool("aborted"));
	}

	[Fact]
	public async Task FreshServer_ListsStoredRounds()
	{
		var match = await CreateMatchAsync("restart_round_match");
		var round = StoredMatches.Round(match, 1, First);
		await Rounds.CreateOrUpdateAsync(round);
		round.EndedAt = Second;
		round.Aborted = true;
		await Rounds.CreateOrUpdateAsync(round);
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
	public async Task UnknownMatchRound_Refused()
	{
		var match = await CreateMatchAsync("unstored_round_match");
		var round = StoredMatches.Round(new MatchRecord { Id = match.Id + 1000, Value = match.Value }, 1, First);

		await Assert.ThrowsAnyAsync<Exception>(() =>
			Rounds.CreateOrUpdateAsync(round));
	}

	[Fact]
	public async Task Round_KeepsBeatmap()
	{
		var match = await CreateMatchAsync("hash_match");
		var hash = StoredScores.Hash("round_beatmap");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First, hash));

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(hash, Assert.Single(listed).BeatmapHash);
	}

	[Fact]
	public async Task Round_KeepsMode()
	{
		var match = await CreateMatchAsync("mode_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First, settings: new MatchSettings
		{
			Mode = GameMode.Taiko
		}));

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(GameMode.Taiko, Assert.Single(listed).Settings.Mode);
	}

	[Fact]
	public async Task Round_KeepsMods()
	{
		var match = await CreateMatchAsync("mods_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First, settings: new MatchSettings
		{
			Mods = GameMods.Hidden
		}));

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(GameMods.Hidden, Assert.Single(listed).Settings.Mods);
	}

	[Fact]
	public async Task Round_KeepsTeamArrangement()
	{
		var match = await CreateMatchAsync("team_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.TeamRound(match, 1, First));

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(GameTeamType.TeamVs, Assert.Single(listed).Settings.TeamType);
	}

	[Fact]
	public async Task Round_KeepsMatch()
	{
		var match = await CreateMatchAsync("owning_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First));

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(match.Id, Assert.Single(listed).Match.Id);
	}

	[Fact]
	public async Task RepeatedRoundNumber_ReplacesStored()
	{
		var match = await CreateMatchAsync("replaced_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First));
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, Second));

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Single(listed);
	}

	[Fact]
	public async Task OtherMatchRounds_LeftOut()
	{
		var match = await CreateMatchAsync("own_match");
		var other = await CreateMatchAsync("other_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First));
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(other, 1, First));

		var listed = await Rounds.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Single(listed);
	}

	[Fact]
	public async Task MatchWithoutRounds_ListsNone()
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