using Basil.Application.Persistence.Multiplayer;
using Basil.Application.Persistence.Scores;
using Basil.Application.Persistence.Users;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Multiplayer;

/// <summary>Contract tests of the storing of which round each score belongs to.</summary>
[Collection("Persistence")]
public sealed class PostgresRoundScoreRepositoryTests : DatabaseTests
{
	private static readonly DateTimeOffset First = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Second = new(2026, 10, 22, 13, 0, 0, TimeSpan.Zero);

	public PostgresRoundScoreRepositoryTests(PersistenceFixture container) : base(container) { }

	private IMatchRepository Matches => Get<IMatchRepository>();

	private IRoundRepository Rounds => Get<IRoundRepository>();

	private IRoundScoreRepository RoundScores => Get<IRoundScoreRepository>();

	private IScoreRepository Scores => Get<IScoreRepository>();

	[Fact]
	public async Task A_stored_round_score_is_listed_under_its_round()
	{
		var match = await CreateMatchAsync("round_score_match");
		var round = await CreateRoundAsync(match, 1);
		var player = await CreatePlayerAsync("rs_player");
		var score = await CreateScoreAsync(player, 1000, First);

		await RoundScores.RecordAsync(new RoundScore { Score = score, Round = round, Team = null },
			TestContext.Current.CancellationToken);

		var listed = await RoundScores.ListAsync(round, TestContext.Current.CancellationToken);

		Assert.Equal(score.Id, Assert.Single(listed).Score.Id);
	}

	[Fact]
	public async Task A_round_score_is_stored()
	{
		var match = await CreateMatchAsync("stored_round_score_match");
		var round = await CreateRoundAsync(match, 1);
		var player = await CreatePlayerAsync("stored_rs");
		var score = await CreateScoreAsync(player, 1000, First);

		await RoundScores.RecordAsync(new RoundScore { Score = score, Round = round, Team = null },
			TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select score_id, match_id, round_number from round_scores where score_id = @id",
			new Dictionary<string, object?> { ["@id"] = score.Id },
			rows => rows.Count == 1 && rows[0].Number("match_id") == match.Id && rows[0].Number("round_number") == 1,
			"the stored round score", TestContext.Current.CancellationToken);

		Assert.Equal(score.Id, rows[0].Number("score_id"));
	}

	[Fact]
	public async Task The_scores_of_a_round_are_listed_in_the_order_they_were_submitted()
	{
		var match = await CreateMatchAsync("ordered_round_score_match");
		var round = await CreateRoundAsync(match, 1);
		var player = await CreatePlayerAsync("ordered_rs");
		var first = await CreateScoreAsync(player, 1000, First);
		var second = await CreateScoreAsync(player, 2000, Second);

		await RoundScores.RecordAsync(new RoundScore { Score = first, Round = round, Team = null },
			TestContext.Current.CancellationToken);
		await RoundScores.RecordAsync(new RoundScore { Score = second, Round = round, Team = null },
			TestContext.Current.CancellationToken);

		var listed = await RoundScores.ListAsync(round, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { first.Id, second.Id }, listed.Select(roundScore => roundScore.Score.Id));
	}

	[Fact]
	public async Task A_round_score_keeps_the_team_it_was_set_for()
	{
		var match = await CreateMatchAsync("team_round_score_match");
		var round = await CreateTeamRoundAsync(match, 1);
		var player = await CreatePlayerAsync("team_rs_player");
		var score = await CreateScoreAsync(player, 1000, First);

		await RoundScores.RecordAsync(new RoundScore { Score = score, Round = round, Team = GameTeam.Blue },
			TestContext.Current.CancellationToken);

		var listed = await RoundScores.ListAsync(round, TestContext.Current.CancellationToken);

		Assert.Equal(GameTeam.Blue, Assert.Single(listed).Team);
	}

	[Fact]
	public async Task The_team_of_a_round_score_is_stored()
	{
		var match = await CreateMatchAsync("stored_team_round_score_match");
		var round = await CreateTeamRoundAsync(match, 1);
		var player = await CreatePlayerAsync("stored_team_rs");
		var score = await CreateScoreAsync(player, 1000, First);

		await RoundScores.RecordAsync(new RoundScore { Score = score, Round = round, Team = GameTeam.Red },
			TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select team from round_scores where score_id = @id",
			new Dictionary<string, object?> { ["@id"] = score.Id },
			rows => rows.Count == 1 && rows[0].Number("team") == (int)GameTeam.Red,
			"the stored team of the round score", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task A_round_score_keeps_the_round_it_was_played_in()
	{
		var match = await CreateMatchAsync("owning_round_score_match");
		var round = await CreateRoundAsync(match, 1);
		var player = await CreatePlayerAsync("owning_rs");
		var score = await CreateScoreAsync(player, 1000, First);

		await RoundScores.RecordAsync(new RoundScore { Score = score, Round = round, Team = null },
			TestContext.Current.CancellationToken);

		var listed = await RoundScores.ListAsync(round, TestContext.Current.CancellationToken);

		Assert.Equal(1, Assert.Single(listed).Round.Index);
	}

	[Fact]
	public async Task The_scores_of_another_round_are_left_out()
	{
		var match = await CreateMatchAsync("own_round_score_match");
		var round = await CreateRoundAsync(match, 1);
		var other = await CreateRoundAsync(match, 2);
		var player = await CreatePlayerAsync("other_rs");
		var score = await CreateScoreAsync(player, 1000, First);

		await RoundScores.RecordAsync(new RoundScore { Score = score, Round = other, Team = null },
			TestContext.Current.CancellationToken);

		var listed = await RoundScores.ListAsync(round, TestContext.Current.CancellationToken);

		Assert.Empty(listed);
	}

	/// <summary>Creates a match the round scores of the test belong to.</summary>
	/// <param name="name">The name of the match.</param>
	/// <returns>The match.</returns>
	private async Task<MatchRecord> CreateMatchAsync(string name)
	{
		return await Matches.CreateAsync(StoredMatches.Data(name, First), TestContext.Current.CancellationToken);
	}

	/// <summary>Creates a round of a match.</summary>
	/// <param name="match">The match the round belongs to.</param>
	/// <param name="index">The number of the round within its match.</param>
	/// <returns>The round.</returns>
	private async Task<Round> CreateRoundAsync(MatchRecord match, int index)
	{
		var round = StoredMatches.Round(match, index, First);
		await Rounds.CreateOrUpdateAsync(round, TestContext.Current.CancellationToken);
		return round;
	}

	/// <summary>Creates a round played in two opposing teams.</summary>
	/// <param name="match">The match the round belongs to.</param>
	/// <param name="index">The number of the round within its match.</param>
	/// <returns>The round.</returns>
	private async Task<Round> CreateTeamRoundAsync(MatchRecord match, int index)
	{
		var round = StoredMatches.TeamRound(match, index, First);
		await Rounds.CreateOrUpdateAsync(round, TestContext.Current.CancellationToken);
		return round;
	}

	/// <summary>Creates the account that played.</summary>
	/// <param name="name">The name of the account.</param>
	/// <returns>The account.</returns>
	private async Task<UserRecord> CreatePlayerAsync(string name)
	{
		return await Get<IUserRepository>().CreateAsync(new UserData { Name = name }, TestContext.Current.CancellationToken);
	}

	/// <summary>Creates a play of the given account.</summary>
	/// <param name="player">The account that played.</param>
	/// <param name="totalScore">The total score the play achieved.</param>
	/// <param name="timestamp">The moment the play happened.</param>
	/// <returns>The play.</returns>
	private async Task<ScoreRecord> CreateScoreAsync(UserRecord player, int totalScore, DateTimeOffset timestamp)
	{
		return await Scores.CreateAsync(StoredScores.Data(player, StoredScores.Hash($"beatmap_{totalScore}"), totalScore,
			timestamp), TestContext.Current.CancellationToken);
	}
}
