using System.Data.Common;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Shared;
using Basil.Application.Shared.Paging;
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
	public async Task CreatedMatch_GetsStoreId()
	{
		var match = await CreateStoredMatchAsync("first_match", First);

		Assert.True(match.Id >= 1);
	}

	[Fact]
	public async Task CreatedMatches_GetIncreasingIds()
	{
		var first = await CreateStoredMatchAsync("older_match", First);
		var second = await CreateStoredMatchAsync("newer_match", Second);

		Assert.True(second.Id > first.Id);
	}

	[Fact]
	public async Task CreatedMatch_Stored()
	{
		var match = await CreateStoredMatchAsync("stored_match", First);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select name from matches where id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id },
			rows => rows.Count == 1 && rows[0].Text("name") == "stored_match",
			"the stored match", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task CreatedMatch_KeepsName()
	{
		var match = await CreateStoredMatchAsync("named_match", First);

		Assert.Equal("named_match", match.Value.Name);
	}

	[Fact]
	public async Task CreatedMatch_KeepsCreator()
	{
		var creator = await Get<IUserRepository>().CreateAsync(new UserData { Name = "creator_user" });

		var match = await Matches.CreateAsync(StoredMatches.Data("owned_match", First, creator));

		Assert.Equal(creator.Id, match.Value.Creator.Id);
	}

	[Fact]
	public async Task MatchById_Found()
	{
		var created = await CreateStoredMatchAsync("found_match", First);

		var found = await Matches.GetAsync(created.Id, TestContext.Current.CancellationToken);

		Assert.NotNull(found);
		Assert.Equal(created.Id, found.Id);
	}

	[Fact]
	public async Task CreatorlessMatch_RefusedByTable()
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
	public async Task UncreatedMatch_NotFound()
	{
		Assert.Null(await Matches.GetAsync(987654, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Update_ChangesReturnedName()
	{
		var match = await CreateStoredMatchAsync("before_rename", First);
		match.Value.Name = "after_rename";

		await Matches.UpdateAsync(match);

		var found = await Matches.GetAsync(match.Id, TestContext.Current.CancellationToken);
		Assert.Equal("after_rename", found!.Value.Name);
	}

	[Fact]
	public async Task Update_ChangesReturnedEnd()
	{
		var match = await CreateStoredMatchAsync("ended_match", First);
		match.Value.EndedAt = Second;

		await Matches.UpdateAsync(match);

		var found = await Matches.GetAsync(match.Id, TestContext.Current.CancellationToken);
		Assert.Equal(Second, found!.Value.EndedAt);
	}

	[Fact]
	public async Task Update_ChangesReturnedPrivacy()
	{
		var match = await CreateStoredMatchAsync("private_match", First);
		match.Value.IsPrivate = true;

		await Matches.UpdateAsync(match);

		var found = await Matches.GetAsync(match.Id, TestContext.Current.CancellationToken);
		Assert.True(found!.Value.IsPrivate);
	}

	[Fact]
	public async Task Update_Stored()
	{
		var match = await CreateStoredMatchAsync("updated_match", First);
		match.Value.Name = "renamed_match";
		match.Value.EndedAt = Second;

		await Matches.UpdateAsync(match);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select name, ended_at from matches where id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id },
			rows => rows.Count == 1 && rows[0].Text("name") == "renamed_match" &&
			        rows[0].Values["ended_at"] is not null &&
			        rows[0].Instant("ended_at") == Second,
			"the stored match", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task Listing_NewestMatchFirst()
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
	public async Task Listing_SkipsEndedMatches()
	{
		await CreateStoredMatchAsync("running_match", First);
		await CreateStoredMatchAsync("ended_match", Second, Second);

		var page = await Matches.ListAsync(new MatchQuery(false), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal("running_match", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task Listing_OnlyEndedMatches()
	{
		await CreateStoredMatchAsync("running_match", First);
		await CreateStoredMatchAsync("ended_match", Second, Second);

		var page = await Matches.ListAsync(new MatchQuery(true), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal("ended_match", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task Listing_SkipsPrivateMatches()
	{
		await CreateStoredMatchAsync("public_match", First);
		await CreateStoredMatchAsync("private_match", Second, isPrivate: true);

		var page = await Matches.ListAsync(new MatchQuery(Private: false), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal("public_match", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task Listing_OnlyPrivateMatches()
	{
		await CreateStoredMatchAsync("public_match", First);
		await CreateStoredMatchAsync("private_match", Second, isPrivate: true);

		var page = await Matches.ListAsync(new MatchQuery(Private: true), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal("private_match", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task Listing_ReturnsRequestedPage()
	{
		await CreateStoredMatchAsync("first_match", First);
		await CreateStoredMatchAsync("second_match", Second);
		await CreateStoredMatchAsync("third_match", Third);

		var page = await Matches.ListAsync(new MatchQuery(), new PageRequest(1, 1),
			TestContext.Current.CancellationToken);

		Assert.True(page.HasNext);
		Assert.Equal("second_match", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task Listing_ReportsMoreMatches()
	{
		await CreateStoredMatchAsync("first_match", First);
		await CreateStoredMatchAsync("second_match", Second);

		var page = await Matches.ListAsync(new MatchQuery(), new PageRequest(0, 1),
			TestContext.Current.CancellationToken);

		Assert.True(page.HasNext);
	}
}