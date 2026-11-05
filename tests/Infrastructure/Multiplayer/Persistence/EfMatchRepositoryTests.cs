using System.Data.Common;
using Basil.Application.Common;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Multiplayer.Tests.Persistence;

/// <summary>Contract tests of the storing of matches.</summary>
[Collection("Persistence")]
public sealed class EfMatchRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
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
	public async Task The_table_refuses_a_match_without_a_creator()
	{
		await Assert.ThrowsAnyAsync<DbException>(() => StoredRows.InsertAsync(ConnectionString,
			"matches", [
				new Dictionary<string, object?>
				{
					["id"] = 100_000_000, ["name"] = "orphan_match", ["creator_id"] = null, ["started_at"] = First,
					["is_private"] = false
				}
			], TestContext.Current.CancellationToken));
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
			rows => rows.Count == 1 && rows[0].Text("name") == "renamed_match" &&
			        rows[0].Values["ended_at"] is not null &&
			        rows[0].Instant("ended_at") == Second,
			"the stored match", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task The_total_of_an_unfiltered_listing_counts_every_stored_match_and_of_a_filtered_one_only_its_own()
	{
		await CreateStoredMatchAsync("running_match", First);
		await CreateStoredMatchAsync("ended_match", Second, Second);

		var all = await Matches.ListAsync(new MatchQuery(), new PageRequest(0, 1),
			TestContext.Current.CancellationToken);
		var running = await Matches.ListAsync(new MatchQuery(false), new PageRequest(0, 1),
			TestContext.Current.CancellationToken);

		Assert.Equal((2, 1), (all.Total, running.Total));
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