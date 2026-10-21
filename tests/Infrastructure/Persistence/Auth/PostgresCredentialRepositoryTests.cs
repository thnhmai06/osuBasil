using System.Text;
using Basil.Application.Persistence.Auth;
using Basil.Application.Persistence.Users;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Auth;

/// <summary>Contract tests of the storing of the login credential of an account.</summary>
[Collection("Persistence")]
public sealed class PostgresCredentialRepositoryTests : DatabaseTests
{
	private const string Password = "correct horse battery staple";

	public PostgresCredentialRepositoryTests(PersistenceFixture container) : base(container) { }

	private ICredentialRepository Credentials => Get<ICredentialRepository>();

	[Fact]
	public async Task A_stored_credential_verifies_the_right_password()
	{
		var user = await Get<IUserRepository>().CreateAsync(new UserData { Name = "cred_user" }, TestContext.Current.CancellationToken);
		var credentials = new Credentials(user, HashOf(Password));

		await Credentials.CreateOrUpdateAsync(credentials, TestContext.Current.CancellationToken);

		Assert.True(await Credentials.VerifyAsync(credentials, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_stored_credential_rejects_a_wrong_password()
	{
		var user = await Get<IUserRepository>().CreateAsync(new UserData { Name = "cred_user" }, TestContext.Current.CancellationToken);
		await Credentials.CreateOrUpdateAsync(new Credentials(user, HashOf(Password)), TestContext.Current.CancellationToken);

		Assert.False(await Credentials.VerifyAsync(new Credentials(user, HashOf("another password")), TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task An_account_without_a_credential_verifies_nothing()
	{
		var user = await Get<IUserRepository>().CreateAsync(new UserData { Name = "no_cred" }, TestContext.Current.CancellationToken);

		Assert.False(await Credentials.VerifyAsync(new Credentials(user, HashOf(Password)), TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_stored_credential_replaces_the_earlier_one()
	{
		var user = await Get<IUserRepository>().CreateAsync(new UserData { Name = "cred_user" }, TestContext.Current.CancellationToken);
		await Credentials.CreateOrUpdateAsync(new Credentials(user, HashOf("the first password")), TestContext.Current.CancellationToken);
		await Credentials.CreateOrUpdateAsync(new Credentials(user, HashOf("the second password")), TestContext.Current.CancellationToken);

		Assert.False(await Credentials.VerifyAsync(new Credentials(user, HashOf("the first password")), TestContext.Current.CancellationToken));
		Assert.True(await Credentials.VerifyAsync(new Credentials(user, HashOf("the second password")), TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task The_stored_credential_is_not_the_password()
	{
		var user = await Get<IUserRepository>().CreateAsync(new UserData { Name = "cred_user" }, TestContext.Current.CancellationToken);
		await Credentials.CreateOrUpdateAsync(new Credentials(user, HashOf(Password)), TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select password_hash from credentials where user_id = @id",
			new Dictionary<string, object?> { ["@id"] = user.Id }, rows => rows.Count == 1,
			"the stored credential", TestContext.Current.CancellationToken);

		var stored = rows[0].Text("password_hash");
		Assert.NotEqual(Password, stored);
		Assert.NotEqual(HashOf(Password).HashValue, stored);
	}

	/// <summary>Computes the digest of a password, the way the osu! client sends it.</summary>
	/// <param name="password">The password to hash.</param>
	private static Md5 HashOf(string password)
	{
		return new Md5(Encoding.UTF8.GetBytes(password));
	}
}
