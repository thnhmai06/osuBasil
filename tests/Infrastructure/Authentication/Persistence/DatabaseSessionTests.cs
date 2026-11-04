using Basil.Application.Common;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Infrastructure.Authentication.Persistence;
using Basil.Infrastructure.Common.Persistence;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Authentication.Tests.Persistence;

/// <summary>Contract tests of the awaited writes of a repository.</summary>
[Collection("Persistence")]
public sealed class DatabaseSessionTests(PersistenceFixture container) : DatabaseTests(container)
{
	private DatabaseSession Database => Get<DatabaseSession>();

	[Fact]
	public async Task A_write_is_stored_when_it_returns()
	{
		var user = await NewUserAsync("session_user");

		await Database.WriteAsync(AddCredential(user.Id), TestContext.Current.CancellationToken);

		var rows = await StoredRows.ReadAsync(ConnectionString, "select user_id from credentials", null,
			TestContext.Current.CancellationToken);
		Assert.Single(rows);
	}

	[Fact]
	public async Task A_write_of_a_value_that_is_already_stored_reports_that_it_exists()
	{
		var user = await NewUserAsync("session_user");
		await Database.WriteAsync(AddCredential(user.Id), TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			Database.WriteAsync(AddCredential(user.Id), TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_transaction_that_fails_stores_nothing()
	{
		var first = await NewUserAsync("first_user");

		await Assert.ThrowsAnyAsync<Exception>(() => Database.TransactAsync(
			async (AuthenticationDbContext context, CancellationToken token) =>
			{
				await AddCredential(first.Id)(context, token);
				await AddCredential(first.Id + 1000)(context, token);
			}, TestContext.Current.CancellationToken));

		var rows = await StoredRows.ReadAsync(ConnectionString, "select user_id from credentials", null,
			TestContext.Current.CancellationToken);
		Assert.Empty(rows);
	}

	[Fact]
	public async Task A_transaction_that_succeeds_stores_every_change()
	{
		var first = await NewUserAsync("first_user");
		var second = await NewUserAsync("second_user");

		await Database.TransactAsync(async (AuthenticationDbContext context, CancellationToken token) =>
		{
			await AddCredential(first.Id)(context, token);
			await AddCredential(second.Id)(context, token);
		}, TestContext.Current.CancellationToken);

		var rows = await StoredRows.ReadAsync(ConnectionString, "select user_id from credentials", null,
			TestContext.Current.CancellationToken);
		Assert.Equal(2, rows.Count);
	}

	[Fact]
	public async Task A_read_sees_what_a_write_stored()
	{
		var user = await NewUserAsync("session_user");
		await Database.WriteAsync(AddCredential(user.Id), TestContext.Current.CancellationToken);

		var count = await Database.ReadAsync((AuthenticationDbContext context) => Task.FromResult(
			context.Credentials.Count()), TestContext.Current.CancellationToken);

		Assert.Equal(1, count);
	}

	private static Func<AuthenticationDbContext, CancellationToken, Task> AddCredential(int userId)
	{
		return (context, _) =>
		{
			context.Credentials.Add(new CredentialRow { UserId = userId, PasswordHash = "hash" });
			return Task.CompletedTask;
		};
	}

	private Task<UserRecord> NewUserAsync(string name)
	{
		return Get<IUserRepository>().CreateAsync(new UserData { Name = name }, TestContext.Current.CancellationToken);
	}
}
