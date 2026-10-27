using Basil.Common.Application;
using Basil.Application.Persistence.Multiplayer;
using Basil.Application.Persistence.Users;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Tests.Support;
using Npgsql;

namespace Basil.Infrastructure.Persistence.Tests.Multiplayer;

/// <summary>Contract tests of the storing of matches.</summary>
[Collection("Persistence")]
public sealed class PostgresMatchRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset First = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Second = new(2026, 10, 22, 13, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Third = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);

	private IMatchRepository Matches => Get<IMatchRepository>();

	[Fact]
	public async Task A_created_match_is_given_an_id_by_the_store()
	{
		var match = await CreateStoredMatchAsync("first_match", First);

		Assert.True(match.Id >= 1);
	}

	[Fact]
	public async Task Created_matches_are_given_increasing_ids()
	{
		var first = await CreateStoredMatchAsync("older_match", First);
		var second = await CreateStoredMatchAsync("newer_match", Second);

		Assert.True(second.Id > first.Id);
	}

	[Fact]
	public async Task A_created_match_is_stored()
	{
		var match = await CreateStoredMatchAsync("stored_match", First);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select name from matches where id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id },
			rows => rows.Count == 1 && rows[0].Text("name") == "stored_match",
			"the stored match", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task A_created_match_keeps_the_name_it_was_created_with()
	{
		var match = await CreateStoredMatchAsync("named_match", First);

		Assert.Equal("named_match", match.Value.Name);
	}

	[Fact]
	public async Task A_created_match_keeps_the_creator_it_was_created_with()
	{
		var creator = await Get<IUserRepository>().CreateAsync(new UserData { Name = "creator_user" },
			TestContext.Current.CancellationToken);

		var match = await Matches.CreateAsync(StoredMatches.Data("owned_match", First, creator),
			TestContext.Current.CancellationToken);

		Assert.Equal(creator.Id, match.Value.Creator.Id);
	}

	[Fact]
	public async Task A_match_is_found_by_its_id()
	{
		var created = await CreateStoredMatchAsync("found_match", First);

		var found = await Matches.GetAsync(created.Id, TestContext.Current.CancellationToken);

		Assert.NotNull(found);
		Assert.Equal(created.Id, found.Id);
	}

	[Fact]
	public async Task Reading_a_stored_match_whose_creator_is_missing_throws()
	{
		int id;
		await using (var connection = new NpgsqlConnection(ConnectionString))
		{
			await connection.OpenAsync(TestContext.Current.CancellationToken);
			await using var command = new NpgsqlCommand(
				"insert into matches (name, creator_id, started_at, is_private) values (@name, null, @started, false) returning id",
				connection);
			command.Parameters.AddWithValue("@name", "orphan_match");
			command.Parameters.AddWithValue("@started", First);
			id = (int)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
		}

		await Assert.ThrowsAsync<InvalidDataException>(async () =>
			await Matches.GetAsync(id, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_match_that_was_never_created_is_not_found()
	{
		Assert.Null(await Matches.GetAsync(987654, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task An_update_changes_the_name_of_the_match_the_store_returns()
	{
		var match = await CreateStoredMatchAsync("before_rename", First);
		match.Value.Name = "after_rename";

		await Matches.UpdateAsync(match, TestContext.Current.CancellationToken);

		var found = await Matches.GetAsync(match.Id, TestContext.Current.CancellationToken);
		Assert.Equal("after_rename", found!.Value.Name);
	}

	[Fact]
	public async Task An_update_changes_the_end_of_the_match_the_store_returns()
	{
		var match = await CreateStoredMatchAsync("ended_match", First);
		match.Value.EndedAt = Second;

		await Matches.UpdateAsync(match, TestContext.Current.CancellationToken);

		var found = await Matches.GetAsync(match.Id, TestContext.Current.CancellationToken);
		Assert.Equal(Second, found!.Value.EndedAt);
	}

	[Fact]
	public async Task An_update_changes_the_privacy_of_the_match_the_store_returns()
	{
		var match = await CreateStoredMatchAsync("private_match", First);
		match.Value.IsPrivate = true;

		await Matches.UpdateAsync(match, TestContext.Current.CancellationToken);

		var found = await Matches.GetAsync(match.Id, TestContext.Current.CancellationToken);
		Assert.True(found!.Value.IsPrivate);
	}

	[Fact]
	public async Task An_update_is_stored()
	{
		var match = await CreateStoredMatchAsync("updated_match", First);
		match.Value.Name = "renamed_match";
		match.Value.EndedAt = Second;

		await Matches.UpdateAsync(match, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select name, ended_at from matches where id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id },
			rows => rows.Count == 1 && rows[0].Text("name") == "renamed_match" && rows[0].Instant("ended_at") == Second,
			"the stored match", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task A_listing_shows_the_newest_match_first()
	{
		await CreateStoredMatchAsync("oldest_match", First);
		await CreateStoredMatchAsync("newest_match", Third);
		await CreateStoredMatchAsync("middle_match", Second);

		var page = await Matches.ListAsync(new MatchQuery(), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(new[] { "newest_match", "middle_match", "oldest_match" },
			page.Items.Select(match => match.Value.Name));
	}

	[Fact]
	public async Task A_listing_leaves_out_the_matches_that_ended()
	{
		await CreateStoredMatchAsync("running_match", First);
		await CreateStoredMatchAsync("ended_match", Second, Second);

		var page = await Matches.ListAsync(new MatchQuery(false), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal("running_match", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task A_listing_shows_only_the_matches_that_ended()
	{
		await CreateStoredMatchAsync("running_match", First);
		await CreateStoredMatchAsync("ended_match", Second, Second);

		var page = await Matches.ListAsync(new MatchQuery(true), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal("ended_match", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task A_listing_leaves_out_the_private_matches()
	{
		await CreateStoredMatchAsync("public_match", First);
		await CreateStoredMatchAsync("private_match", Second, isPrivate: true);

		var page = await Matches.ListAsync(new MatchQuery(Private: false), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal("public_match", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task A_listing_shows_only_the_private_matches()
	{
		await CreateStoredMatchAsync("public_match", First);
		await CreateStoredMatchAsync("private_match", Second, isPrivate: true);

		var page = await Matches.ListAsync(new MatchQuery(Private: true), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal("private_match", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task A_listing_returns_the_requested_part()
	{
		await CreateStoredMatchAsync("first_match", First);
		await CreateStoredMatchAsync("second_match", Second);
		await CreateStoredMatchAsync("third_match", Third);

		var page = await Matches.ListAsync(new MatchQuery(), new PageRequest(1, 1),
			TestContext.Current.CancellationToken);

		Assert.Equal(3, page.Total);
		Assert.Equal("second_match", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task A_listing_tells_how_many_matches_match()
	{
		await CreateStoredMatchAsync("first_match", First);
		await CreateStoredMatchAsync("second_match", Second);

		var page = await Matches.ListAsync(new MatchQuery(), new PageRequest(0, 1),
			TestContext.Current.CancellationToken);

		Assert.Equal(2, page.Total);
	}
}