using Basil.Application.Shared;
using Basil.Application.Shared.Exceptions;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Infrastructure.Authentication.Persistence;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Authentication.Tests.Persistence;

/// <summary>Contract tests of the awaited writes of a repository.</summary>
[Collection("Persistence")]
public sealed class DatabaseSessionTests(PersistenceFixture container) : DatabaseTests(container)
{
	private DatabaseSession Database => Get<DatabaseSession>();

	[Fact]
	public async Task Write_StoredOnReturn()
	{
		var user = await NewUserAsync("session_user");

		await Database.WriteAsync(AddCredential(user.Id));

		var rows = await StoredRows.ReadAsync(ConnectionString, "select user_id from credentials", null,
			TestContext.Current.CancellationToken);
		Assert.Single(rows);
	}

	[Fact]
	public async Task DuplicateWrite_ReportsExists()
	{
		var user = await NewUserAsync("session_user");
		await Database.WriteAsync(AddCredential(user.Id));

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			Database.WriteAsync(AddCredential(user.Id)));
	}

	[Fact]
	public async Task FailedTransaction_StoresNothing()
	{
		var first = await NewUserAsync("first_user");

		await Assert.ThrowsAnyAsync<Exception>(() => Database.TransactAsync(
			async (AuthenticationDbContext context) =>
			{
				await AddCredential(first.Id)(context);
				await AddCredential(first.Id + 1000)(context);
			}));

		var rows = await StoredRows.ReadAsync(ConnectionString, "select user_id from credentials", null,
			TestContext.Current.CancellationToken);
		Assert.Empty(rows);
	}

	[Fact]
	public async Task SuccessfulTransaction_StoresEveryChange()
	{
		var first = await NewUserAsync("first_user");
		var second = await NewUserAsync("second_user");

		await Database.TransactAsync(async (AuthenticationDbContext context) =>
		{
			await AddCredential(first.Id)(context);
			await AddCredential(second.Id)(context);
		});

		var rows = await StoredRows.ReadAsync(ConnectionString, "select user_id from credentials", null,
			TestContext.Current.CancellationToken);
		Assert.Equal(2, rows.Count);
	}

	[Fact]
	public async Task Read_SeesStoredWrite()
	{
		var user = await NewUserAsync("session_user");
		await Database.WriteAsync(AddCredential(user.Id));

		var count = await Database.ReadAsync((AuthenticationDbContext context) => Task.FromResult(
			context.Credentials.Count()), TestContext.Current.CancellationToken);

		Assert.Equal(1, count);
	}

	private static Func<AuthenticationDbContext, Task> AddCredential(int userId)
	{
		return context =>
		{
			context.Credentials.Add(new CredentialRow { UserId = userId, PasswordHash = "hash" });
			return Task.CompletedTask;
		};
	}

	private Task<UserRecord> NewUserAsync(string name)
	{
		return Get<IUserRepository>().CreateAsync(new UserData { Name = name });
	}
}