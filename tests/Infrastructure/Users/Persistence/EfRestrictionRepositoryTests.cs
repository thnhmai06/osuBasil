using Basil.Application.Common;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Users.Tests.Persistence;

/// <summary>Contract tests of the storing of the restrictions of an account.</summary>
[Collection("Persistence")]
public sealed class EfRestrictionRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset Moment = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

	private IRestrictionRepository Restrictions => Get<IRestrictionRepository>();

	[Fact]
	public async Task A_listing_reads_no_more_restrictions_than_the_ceiling()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "many_bans" }, TestContext.Current.CancellationToken);
		await StoredRows.ExecuteAsync(ConnectionString,
			"insert into restrictions (user_id, permissions, starts_at) " +
			"select @user, @permissions, @start + g * interval '1 second' from generate_series(1, @count) g",
			new Dictionary<string, object?>
			{
				["@user"] = user.Id, ["@permissions"] = (long)RestrictionData.Silence, ["@start"] = Moment,
				["@count"] = ListLimits.Ceiling + 1
			}, TestContext.Current.CancellationToken);

		var listed = await Restrictions.ListAsync(user, TestContext.Current.CancellationToken);

		Assert.Equal(ListLimits.Ceiling, listed.Count);
	}

	[Fact]
	public async Task A_created_restriction_is_given_an_id_by_the_store()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "restricted" }, TestContext.Current.CancellationToken);

		var restriction = await Restrictions.CreateAsync(new RestrictionData
		{
			User = user,
			Permissions = RestrictionData.Silence,
			StartsAt = Moment
		}, TestContext.Current.CancellationToken);

		Assert.True(restriction.Id >= 1);
	}

	[Fact]
	public async Task A_restriction_is_found_by_id()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "restricted" }, TestContext.Current.CancellationToken);
		var created = await CreateAsync(user, Moment);

		var found = await Restrictions.GetAsync(created.Id, TestContext.Current.CancellationToken);

		Assert.NotNull(found);
		Assert.Equal(created.Id, found.Id);
		Assert.Equal(user.Id, found.Value.User.Id);
		Assert.Equal(RestrictionData.Silence, found.Value.Permissions);
	}

	[Fact]
	public async Task A_user_restrictions_are_listed()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "restricted" }, TestContext.Current.CancellationToken);
		var created = await CreateAsync(user, Moment);

		var listed = await Restrictions.ListAsync(user, TestContext.Current.CancellationToken);

		Assert.Equal(created.Id, Assert.Single(listed).Id);
	}

	[Fact]
	public async Task A_restriction_of_an_account_the_database_does_not_hold_is_refused_to_the_caller()
	{
		var unknown = new UserRecord { Id = 4242, Value = new UserData { Name = "not_stored" } };

		await Assert.ThrowsAnyAsync<Exception>(() => CreateAsync(unknown, Moment));
	}

	[Fact]
	public async Task An_update_changes_the_permissions_of_a_restriction()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "restricted" }, TestContext.Current.CancellationToken);
		var created = await CreateAsync(user, Moment);
		created.Value.Permissions = RestrictionData.Restricted;

		await Restrictions.UpdateAsync(created, TestContext.Current.CancellationToken);

		var found = await Restrictions.GetAsync(created.Id, TestContext.Current.CancellationToken);
		Assert.Equal(RestrictionData.Restricted, found!.Value.Permissions);
	}

	[Fact]
	public async Task An_update_changes_the_end_of_a_restriction()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "restricted" }, TestContext.Current.CancellationToken);
		var created = await CreateAsync(user, Moment);
		created.Value.EndsAt = Moment.AddHours(2);

		await Restrictions.UpdateAsync(created, TestContext.Current.CancellationToken);

		var found = await Restrictions.GetAsync(created.Id, TestContext.Current.CancellationToken);
		Assert.Equal(Moment.AddHours(2), found!.Value.EndsAt);
	}

	[Fact]
	public async Task An_update_is_stored()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "restricted" }, TestContext.Current.CancellationToken);
		var created = await CreateAsync(user, Moment);
		created.Value.EndsAt = Moment.AddHours(2);
		created.Value.Permissions = RestrictionData.Restricted;
		await Restrictions.UpdateAsync(created, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select permissions, ends_at from restrictions where id = @id",
			new Dictionary<string, object?> { ["@id"] = created.Id },
			rows => rows.Count == 1 && rows[0].Number("permissions") == (long)RestrictionData.Restricted,
			"the stored restriction", TestContext.Current.CancellationToken);

		Assert.Equal(Moment.AddHours(2), rows[0].Instant("ends_at"));
	}

	[Fact]
	public async Task A_deleted_restriction_is_removed_from_the_database_and_history()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "restricted" }, TestContext.Current.CancellationToken);
		var created = await CreateAsync(user, Moment);

		await Restrictions.DeleteAsync(created, TestContext.Current.CancellationToken);

		var rows = await StoredRows.ReadAsync(ConnectionString,
			"select id from restrictions where id = @id",
			new Dictionary<string, object?> { ["@id"] = created.Id }, TestContext.Current.CancellationToken);
		Assert.Empty(rows);
		Assert.Empty(await Restrictions.ListAsync(user, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_user_restrictions_are_listed_oldest_first()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "restricted" }, TestContext.Current.CancellationToken);
		await CreateAsync(user, Moment.AddHours(2));
		await CreateAsync(user, Moment);
		await CreateAsync(user, Moment.AddHours(1));

		var listed = await Restrictions.ListAsync(user, TestContext.Current.CancellationToken);

		Assert.Equal(
			new[] { Moment, Moment.AddHours(1), Moment.AddHours(2) },
			listed.Select(restriction => restriction.Value.StartsAt));
	}

	[Fact]
	public async Task Restrictions_that_start_together_are_listed_by_id()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "restricted" }, TestContext.Current.CancellationToken);
		var first = await CreateAsync(user, Moment);
		var second = await CreateAsync(user, Moment);

		var listed = await Restrictions.ListAsync(user, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { first.Id, second.Id }, listed.Select(restriction => restriction.Id));
	}

	/// <summary>Creates a restriction of an account.</summary>
	/// <param name="user">The restricted account.</param>
	/// <param name="startsAt">The moment the restriction starts.</param>
	private async Task<UserRestriction> CreateAsync(UserRecord user, DateTimeOffset startsAt)
	{
		return await Restrictions.CreateAsync(new RestrictionData
		{
			User = user,
			Permissions = RestrictionData.Silence,
			StartsAt = startsAt
		}, TestContext.Current.CancellationToken);
	}
}