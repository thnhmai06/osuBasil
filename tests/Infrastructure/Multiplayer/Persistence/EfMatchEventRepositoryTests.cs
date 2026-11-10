using Basil.Application.Common;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Multiplayer.Tests.Persistence;

/// <summary>Contract tests of the storing of what happened in matches.</summary>
[Collection("Persistence")]
public sealed class EfMatchEventRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset First = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Second = new(2026, 10, 22, 13, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Third = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);

	private IMatchRepository Matches => Get<IMatchRepository>();

	private IMatchEventRepository Events => Get<IMatchEventRepository>();

	[Fact]
	public async Task A_listing_reads_no_more_events_than_the_ceiling()
	{
		var match = await CreateMatchAsync("busy_event_match");
		await StoredRows.InsertAsync(ConnectionString, "match_events",
			Enumerable.Range(1, ListLimits.Ceiling + 1).Select(g =>
				(IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
				{
					["id"] = 100_000_000L + g, ["match_id"] = match.Id, ["type"] = (int)MatchEventType.Created,
					["timestamp"] = First.AddSeconds(g)
				}), TestContext.Current.CancellationToken);

		var listed = await Events.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(ListLimits.Ceiling, listed.Count);
	}

	[Fact]
	public async Task A_recorded_event_is_stored()
	{
		var match = await CreateMatchAsync("event_match");
		await Events.RecordAsync(StoredMatches.Event(match, MatchEventType.Created, First));

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select type, timestamp from match_events where match_id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id },
			rows => rows.Count == 1 && rows[0].Number("type") == (int)MatchEventType.Created,
			"the recorded event", TestContext.Current.CancellationToken);

		Assert.Equal(First, rows[0].Instant("timestamp"));
	}

	[Fact]
	public async Task An_event_of_a_match_that_is_not_stored_is_refused_to_the_caller()
	{
		var match = await CreateMatchAsync("unstored_event_match");
		var unstored = new MatchRecord { Id = match.Id + 1000, Value = match.Value };

		await Assert.ThrowsAnyAsync<Exception>(() => Events.RecordAsync(
			StoredMatches.Event(unstored, MatchEventType.Created, First)));
	}

	[Fact]
	public async Task The_events_of_a_match_are_listed_in_the_order_they_happened()
	{
		var match = await CreateMatchAsync("ordered_event_match");
		await Events.RecordAsync(StoredMatches.Event(match, MatchEventType.Closed, Third));
		await Events.RecordAsync(StoredMatches.Event(match, MatchEventType.Created, First));
		await Events.RecordAsync(StoredMatches.Event(match, MatchEventType.PlayerJoined, Second));
		await WaitForEventsAsync(match, 3);

		var listed = await Events.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { MatchEventType.Created, MatchEventType.PlayerJoined, MatchEventType.Closed },
			listed.Select(matchEvent => matchEvent.Type));
	}

	[Fact]
	public async Task A_recorded_event_names_the_account_that_performed_it()
	{
		var match = await CreateMatchAsync("actor_event_match");
		var actor = await Get<IUserRepository>().CreateAsync(new UserData { Name = "event_actor" });
		await Events.RecordAsync(StoredMatches.Event(match, MatchEventType.PlayerJoined, First, actor));
		await WaitForEventsAsync(match, 1);

		var listed = await Events.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(actor.Id, Assert.Single(listed).Actor?.Id);
	}

	[Fact]
	public async Task A_recorded_event_names_the_account_it_affected()
	{
		var match = await CreateMatchAsync("target_event_match");
		var target = await Get<IUserRepository>().CreateAsync(new UserData { Name = "event_target" });
		await Events.RecordAsync(StoredMatches.Event(match, MatchEventType.PlayerLeft, First, null, target));
		await WaitForEventsAsync(match, 1);

		var listed = await Events.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal(target.Id, Assert.Single(listed).Target?.Id);
	}

	[Fact]
	public async Task A_recorded_event_keeps_its_detail()
	{
		var match = await CreateMatchAsync("detailed_event_match");
		await Events.RecordAsync(StoredMatches.Event(match, MatchEventType.RefAdded, First, detail: "A referee."));
		await WaitForEventsAsync(match, 1);

		var listed = await Events.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Equal("A referee.", Assert.Single(listed).Detail);
	}

	[Fact]
	public async Task The_events_of_another_match_are_left_out()
	{
		var match = await CreateMatchAsync("own_event_match");
		var other = await CreateMatchAsync("other_event_match");
		await Events.RecordAsync(StoredMatches.Event(match, MatchEventType.Created, First));
		await Events.RecordAsync(StoredMatches.Event(other, MatchEventType.Created, First));
		await WaitForEventsAsync(match, 1);

		var listed = await Events.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Single(listed);
	}

	[Fact]
	public async Task A_match_without_an_event_lists_none()
	{
		var match = await CreateMatchAsync("empty_event_match");

		var listed = await Events.ListAsync(match, TestContext.Current.CancellationToken);

		Assert.Empty(listed);
	}

	/// <summary>Creates a match the events of the test belong to.</summary>
	/// <param name="name">The name of the match.</param>
	/// <returns>The match.</returns>
	private async Task<MatchRecord> CreateMatchAsync(string name)
	{
		return await CreateStoredMatchAsync(name, First);
	}

	/// <summary>Waits until the given number of events of a match is stored.</summary>
	/// <param name="match">The match whose events are waited for.</param>
	/// <param name="count">How many events to wait for.</param>
	private async Task WaitForEventsAsync(MatchRecord match, int count)
	{
		await StoredRows.WaitForAsync(ConnectionString, "select id from match_events where match_id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id },
			rows => rows.Count == count, $"{count} stored events", TestContext.Current.CancellationToken);
	}
}