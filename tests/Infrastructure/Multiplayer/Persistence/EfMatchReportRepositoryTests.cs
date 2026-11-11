using System.Text.Json;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Scores.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Multiplayer.Tests.Persistence;

/// <summary>Contract tests of the report of what happened in a match.</summary>
[Collection("Persistence")]
public sealed class EfMatchReportRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset First = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Second = new(2026, 10, 22, 13, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Third = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);

	private IMatchRepository Matches => Get<IMatchRepository>();

	private IRoundRepository Rounds => Get<IRoundRepository>();

	private IRoundScoreRepository RoundScores => Get<IRoundScoreRepository>();

	private IMatchEventRepository Events => Get<IMatchEventRepository>();

	private IMatchReportRepository Reports => Get<IMatchReportRepository>();

	private IScoreRepository Scores => Get<IScoreRepository>();

	[Fact]
	public async Task Report_NamesItsMatch()
	{
		var match = await CreateMatchAsync("reported_match");

		var report = await Reports.GetAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(match.Id, report.Match.Id);
	}

	[Fact]
	public async Task StaleCopy_ReportShowsStored()
	{
		var match = await CreateMatchAsync("ended_report_match");
		var stale = (await Matches.GetAsync(match.Id, TestContext.Current.CancellationToken))!;
		match.Value.EndedAt = Second;
		await Matches.UpdateAsync(match);

		var report = await Reports.GetAsync(stale, TestContext.Current.CancellationToken);

		Assert.Equal(Second, report.Match.Value.EndedAt);
	}

	[Fact]
	public async Task Report_HoldsMatchRounds()
	{
		var match = await CreateMatchAsync("round_report_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First));

		var report = await Reports.GetAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(1, Assert.Single(report.Records.OfType<Round>()).Index);
	}

	[Fact]
	public async Task Report_HoldsMatchEvents()
	{
		var match = await CreateMatchAsync("event_report_match");
		await Events.RecordAsync(StoredMatches.Event(match, MatchEventType.Created, First));

		var report = await Reports.GetAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(MatchEventType.Created, Assert.Single(report.Records.OfType<MatchEvent>()).Type);
	}

	[Fact]
	public async Task Report_HoldsOrderedHistory()
	{
		var match = await CreateMatchAsync("ordered_report_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, Second));
		await Events.RecordAsync(StoredMatches.Event(match, MatchEventType.Created, First));
		await Events.RecordAsync(StoredMatches.Event(match, MatchEventType.Closed, Third));

		var report = await Reports.GetAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { MatchEventType.Created.ToString(), "round", MatchEventType.Closed.ToString() },
			report.Records.Select(record => record is MatchEvent matchEvent ? matchEvent.Type.ToString() : "round"));
	}

	[Fact]
	public async Task Report_ShowsStoredDecision()
	{
		var match = await CreateMatchAsync("decided_report_match");
		var round = StoredMatches.Round(match, 1, First);
		var winner = await CreatePlayerAsync("round_winner");
		var loser = await CreatePlayerAsync("round_loser");
		var won = await RecordScoreAsync(round, winner, 5000, First);
		var lost = await RecordScoreAsync(round, loser, 1000, First);
		round.Result = RoundResult.Decide(round, [lost, won]);
		await Rounds.CreateOrUpdateAsync(round);

		var report = await Reports.GetAsync(match, TestContext.Current.CancellationToken);

		var decision = Assert.IsType<HeadToHeadResult>(Assert.Single(report.Records.OfType<Round>()).Result);
		Assert.Equal(winner.Id, decision.WinnerId);
		Assert.Equal([won.Score.Id, lost.Score.Id], decision.Scores.Select(s => s.Score.Id));
	}

	[Fact]
	public async Task RoundDecision_SurvivesRestart()
	{
		var match = await CreateMatchAsync("decision_round_match");
		var round = StoredMatches.Round(match, 1, First);
		var first = await CreatePlayerAsync("restart_first");
		var second = await CreatePlayerAsync("restart_second");
		var low = await RecordScoreAsync(round, first, 1000, First);
		var high = await RecordScoreAsync(round, second, 5000, First);
		round.Result = RoundResult.Restore(round, [low, high], [low.Score.Id, high.Score.Id], 1500.5, false,
			first.Id, null, null, null);
		await Rounds.CreateOrUpdateAsync(round);
		await StoredRows.WaitForAsync(ConnectionString, "select result from rounds where match_id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id },
			rows => rows.Count == 1 && rows[0]["result"] is not null,
			"the decision", TestContext.Current.CancellationToken);

		await using var other = new StorageComposition(ConnectionString);
		var decision = Assert.IsType<HeadToHeadResult>(Assert.Single(await other.Get<IRoundRepository>().ListAsync(
			match, TestContext.Current.CancellationToken)).Result);

		Assert.Equal([low.Score.Id, high.Score.Id], decision.Scores.Select(s => s.Score.Id));
		Assert.Equal((first.Id, 1500.5, false), (decision.WinnerId, decision.Margin, decision.IsDraw));
	}

	[Fact]
	public async Task StoredDecision_KeepsItsJsonShape()
	{
		var match = await CreateMatchAsync("json_round_match");
		var round = StoredMatches.Round(match, 1, First);
		var winner = await CreatePlayerAsync("json_winner");
		var loser = await CreatePlayerAsync("json_loser");
		var won = await RecordScoreAsync(round, winner, 5000, First);
		var lost = await RecordScoreAsync(round, loser, 1000, First);
		round.Result = RoundResult.Decide(round, [lost, won]);
		await Rounds.CreateOrUpdateAsync(round);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select result from rounds where match_id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id },
			rows => rows.Count == 1 && rows[0]["result"] is not null,
			"the decision", TestContext.Current.CancellationToken);

		using var json = JsonDocument.Parse(Convert.ToString(rows[0]["result"])!);
		var stored = json.RootElement;
		Assert.Equal([won.Score.Id, lost.Score.Id],
			stored.GetProperty("ScoreOrder").EnumerateArray().Select(e => e.GetInt32()));
		Assert.Equal(winner.Id, stored.GetProperty("WinnerId").GetInt32());
		Assert.Equal(JsonValueKind.Null, stored.GetProperty("Winner").ValueKind);
		Assert.Equal(4000, stored.GetProperty("Margin").GetDouble());
		Assert.False(stored.GetProperty("IsDraw").GetBoolean());
		Assert.Equal(JsonValueKind.Null, stored.GetProperty("RedScore").ValueKind);
		Assert.Equal(JsonValueKind.Null, stored.GetProperty("BlueScore").ValueKind);
	}

	[Fact]
	public async Task OutdatedStoredDecision_StillShown()
	{
		var match = await CreateMatchAsync("rules_changed_match");
		var round = StoredMatches.Round(match, 1, First);
		var first = await CreatePlayerAsync("first_player");
		var second = await CreatePlayerAsync("second_player");
		var low = await RecordScoreAsync(round, first, 1000, First);
		var high = await RecordScoreAsync(round, second, 5000, First);

		// The stored decision names the player with the lower score as the winner; a fresh decision would not.
		round.Result = RoundResult.Restore(round, [low, high], [low.Score.Id, high.Score.Id], 4000, false,
			first.Id, null, null, null);
		await Rounds.CreateOrUpdateAsync(round);

		var report = await Reports.GetAsync(match, TestContext.Current.CancellationToken);

		var decision = Assert.IsType<HeadToHeadResult>(Assert.Single(report.Records.OfType<Round>()).Result);
		Assert.Equal(first.Id, decision.WinnerId);
	}

	[Fact]
	public async Task UndecidedRound_LeftWithoutResult()
	{
		var match = await CreateMatchAsync("undecided_report_match");
		var round = StoredMatches.Round(match, 1, First);
		await Rounds.CreateOrUpdateAsync(round);
		await RecordScoreAsync(round, await CreatePlayerAsync("undecided"), 5000, First);

		var report = await Reports.GetAsync(match, TestContext.Current.CancellationToken);

		Assert.Null(Assert.Single(report.Records.OfType<Round>()).Result);
		Assert.Null((await Rounds.ListAsync(match, TestContext.Current.CancellationToken)).Single().Result);
	}

	[Fact]
	public async Task RoundlessMatchReport_HoldsNoRecords()
	{
		var match = await CreateMatchAsync("empty_report_match");

		var report = await Reports.GetAsync(match, TestContext.Current.CancellationToken);

		Assert.Empty(report.Records);
	}

	/// <summary>Creates a match the report of the test describes.</summary>
	/// <param name="name">The name of the match.</param>
	/// <returns>The match.</returns>
	private async Task<MatchRecord> CreateMatchAsync(string name)
	{
		return await CreateStoredMatchAsync(name, First);
	}

	/// <summary>Creates the account that played.</summary>
	/// <param name="name">The name of the account.</param>
	/// <returns>The account.</returns>
	private async Task<UserRecord> CreatePlayerAsync(string name)
	{
		return await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = name });
	}

	/// <summary>Records a play in a round.</summary>
	/// <param name="round">The round the play was made in.</param>
	/// <param name="player">The account that played.</param>
	/// <param name="totalScore">The total score the play achieved.</param>
	/// <param name="timestamp">The moment the play happened.</param>
	/// <returns>The relation between the round and the score.</returns>
	private async Task<RoundScore> RecordScoreAsync(Round round, UserRecord player, int totalScore,
		DateTimeOffset timestamp)
	{
		var score = await Scores.CreateAsync(StoredScores.Data(player, StoredScores.Hash($"beatmap_{totalScore}"),
			totalScore, timestamp));
		return await RoundScores.RecordAsync(new RoundScore(score, round, null));
	}
}