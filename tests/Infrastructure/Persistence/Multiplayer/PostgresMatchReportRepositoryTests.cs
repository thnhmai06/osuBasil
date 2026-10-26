using Basil.Application.Persistence.Multiplayer;
using Basil.Application.Persistence.Scores;
using Basil.Application.Persistence.Users;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Multiplayer;

/// <summary>Contract tests of the report of what happened in a match.</summary>
[Collection("Persistence")]
public sealed class PostgresMatchReportRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
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
	public async Task A_report_names_the_match_it_describes()
	{
		var match = await CreateMatchAsync("reported_match");

		var report = await Reports.GetAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(match.Id, report.Match.Id);
	}

	[Fact]
	public async Task A_report_holds_the_rounds_of_the_match()
	{
		var match = await CreateMatchAsync("round_report_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, First), TestContext.Current.CancellationToken);

		var report = await Reports.GetAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(1, Assert.Single(report.Records.OfType<Round>()).Index);
	}

	[Fact]
	public async Task A_report_holds_the_events_of_the_match()
	{
		var match = await CreateMatchAsync("event_report_match");
		await Events.RecordAsync(StoredMatches.Event(match, MatchEventType.Created, First),
			TestContext.Current.CancellationToken);

		var report = await Reports.GetAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(MatchEventType.Created, Assert.Single(report.Records.OfType<MatchEvent>()).Type);
	}

	[Fact]
	public async Task A_report_holds_the_rounds_and_the_events_in_the_order_they_happened()
	{
		var match = await CreateMatchAsync("ordered_report_match");
		await Rounds.CreateOrUpdateAsync(StoredMatches.Round(match, 1, Second), TestContext.Current.CancellationToken);
		await Events.RecordAsync(StoredMatches.Event(match, MatchEventType.Created, First),
			TestContext.Current.CancellationToken);
		await Events.RecordAsync(StoredMatches.Event(match, MatchEventType.Closed, Third),
			TestContext.Current.CancellationToken);

		var report = await Reports.GetAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { MatchEventType.Created.ToString(), "round", MatchEventType.Closed.ToString() },
			report.Records.Select(record => record is MatchEvent matchEvent ? matchEvent.Type.ToString() : "round"));
	}

	[Fact]
	public async Task A_report_decides_the_result_of_a_round_from_its_scores()
	{
		var match = await CreateMatchAsync("decided_report_match");
		var round = StoredMatches.Round(match, 1, First);
		await Rounds.CreateOrUpdateAsync(round, TestContext.Current.CancellationToken);
		var winner = await CreatePlayerAsync("round_winner");
		var loser = await CreatePlayerAsync("round_loser");
		await RecordScoreAsync(round, winner, 5000, First);
		await RecordScoreAsync(round, loser, 1000, First);

		var report = await Reports.GetAsync(match, TestContext.Current.CancellationToken);

		var result = Assert.IsType<HeadToHeadResult>(Assert.Single(report.Records.OfType<Round>()).Result);
		Assert.Equal(winner.Id, result.WinnerId);
	}

	[Fact]
	public async Task A_report_of_a_match_without_a_round_holds_no_records()
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
		return await Get<IUserRepository>().CreateAsync(new UserData { Name = name }, TestContext.Current.CancellationToken);
	}

	/// <summary>Records a play in a round.</summary>
	/// <param name="round">The round the play was made in.</param>
	/// <param name="player">The account that played.</param>
	/// <param name="totalScore">The total score the play achieved.</param>
	/// <param name="timestamp">The moment the play happened.</param>
	private async Task RecordScoreAsync(Round round, UserRecord player, int totalScore, DateTimeOffset timestamp)
	{
		var score = await Scores.CreateAsync(StoredScores.Data(player, StoredScores.Hash($"beatmap_{totalScore}"),
			totalScore, timestamp), TestContext.Current.CancellationToken);
		await RoundScores.RecordAsync(new RoundScore(score, round, null),
			TestContext.Current.CancellationToken);
	}
}
