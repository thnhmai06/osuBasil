using System.Text;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Authentication.Tests.Persistence;

/// <summary>Contract tests of the storing of the login credential of an account.</summary>
[Collection("Persistence")]
public sealed class EfCredentialRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private const string Password = "correct horse battery staple";

	private ICredentialRepository Credentials => Get<ICredentialRepository>();

	[Fact]
	public async Task StoredCredential_VerifiesRightPassword()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "cred_user" });
		var credentials = new Credentials(user, HashOf(Password));

		await Credentials.CreateOrUpdateAsync(credentials);

		Assert.True(await Credentials.VerifyAsync(credentials, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task StoredCredential_RejectsWrongPassword()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "cred_user" });
		await Credentials.CreateOrUpdateAsync(new Credentials(user, HashOf(Password)));

		Assert.False(await Credentials.VerifyAsync(new Credentials(user, HashOf("another password")),
			TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task AccountWithoutCredential_VerifiesNothing()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "no_cred" });

		Assert.False(await Credentials.VerifyAsync(new Credentials(user, HashOf(Password)),
			TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task StoredCredential_ReplacesEarlier()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "cred_user" });
		await Credentials.CreateOrUpdateAsync(new Credentials(user, HashOf("the first password")));
		await Credentials.CreateOrUpdateAsync(new Credentials(user, HashOf("the second password")));

		Assert.False(await Credentials.VerifyAsync(new Credentials(user, HashOf("the first password")),
			TestContext.Current.CancellationToken));
		Assert.True(await Credentials.VerifyAsync(new Credentials(user, HashOf("the second password")),
			TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task DeletedCredential_VerifiesNothing()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "cred_user" });
		await Credentials.CreateOrUpdateAsync(new Credentials(user, HashOf(Password)));

		await Credentials.DeleteAsync(user);

		Assert.False(await Credentials.VerifyAsync(new Credentials(user, HashOf(Password)),
			TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task DeletedCredential_RemovedFromDatabase()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "cred_user" });
		await Credentials.CreateOrUpdateAsync(new Credentials(user, HashOf(Password)));
		await StoredRows.WaitForAsync(ConnectionString, "select user_id from credentials where user_id = @id",
			new Dictionary<string, object?> { ["@id"] = user.Id }, rows => rows.Count == 1,
			"the stored credential", TestContext.Current.CancellationToken);

		await Credentials.DeleteAsync(user);

		await StoredRows.WaitForAsync(ConnectionString, "select user_id from credentials where user_id = @id",
			new Dictionary<string, object?> { ["@id"] = user.Id }, rows => rows.Count == 0,
			"no credential", TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task DeleteMissingCredential_ChangesNothing()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "no_cred" });

		await Credentials.DeleteAsync(user);

		Assert.False(await Credentials.VerifyAsync(new Credentials(user, HashOf(Password)),
			TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task StoredCredential_NotPassword()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "cred_user" });
		await Credentials.CreateOrUpdateAsync(new Credentials(user, HashOf(Password)));

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select password_hash from credentials where user_id = @id",
			new Dictionary<string, object?> { ["@id"] = user.Id }, rows => rows.Count == 1,
			"the stored credential", TestContext.Current.CancellationToken);

		var stored = rows[0].Text("password_hash");
		Assert.NotEqual(Password, stored);
		Assert.NotEqual(HashOf(Password).HashValue, stored);
	}

	[Fact]
	public async Task StoredCredential_PresentOnReturn()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "cred_user" });

		await Credentials.CreateOrUpdateAsync(new Credentials(user, HashOf(Password)));

		var rows = await StoredRows.ReadAsync(ConnectionString, "select user_id from credentials where user_id = @id",
			new Dictionary<string, object?> { ["@id"] = user.Id }, TestContext.Current.CancellationToken);
		Assert.Single(rows);
	}

	[Fact]
	public async Task MissingAccountCredential_Refused()
	{
		var ghost = new UserRecord { Id = 9999, Value = new UserData { Name = "ghost" } };

		await Assert.ThrowsAnyAsync<Exception>(() => Credentials.CreateOrUpdateAsync(
			new Credentials(ghost, HashOf(Password))));

		Assert.False(await Credentials.VerifyAsync(new Credentials(ghost, HashOf(Password)),
			TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task ReplacedPassword_EffectiveAfterVerified()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "cred_user" });
		await Credentials.CreateOrUpdateAsync(new Credentials(user, HashOf("the first password")));
		Assert.True(await Credentials.VerifyAsync(new Credentials(user, HashOf("the first password")),
			TestContext.Current.CancellationToken));

		await Credentials.CreateOrUpdateAsync(new Credentials(user, HashOf("the second password")));

		Assert.False(await Credentials.VerifyAsync(new Credentials(user, HashOf("the first password")),
			TestContext.Current.CancellationToken));
		Assert.True(await Credentials.VerifyAsync(new Credentials(user, HashOf("the second password")),
			TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task DeletedAfterVerify_VerifiesNothing()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "cred_user" });
		await Credentials.CreateOrUpdateAsync(new Credentials(user, HashOf(Password)));
		Assert.True(await Credentials.VerifyAsync(new Credentials(user, HashOf(Password)),
			TestContext.Current.CancellationToken));

		await Credentials.DeleteAsync(user);

		Assert.False(await Credentials.VerifyAsync(new Credentials(user, HashOf(Password)),
			TestContext.Current.CancellationToken));
	}

	/// <summary>Computes the digest of a password, the way the osu! client sends it.</summary>
	/// <param name="password">The password to hash.</param>
	private static Md5 HashOf(string password)
	{
		return new Md5(Encoding.UTF8.GetBytes(password));
	}
}