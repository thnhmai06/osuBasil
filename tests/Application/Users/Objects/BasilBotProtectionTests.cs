using Basil.Application.Users.Objects;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Objects;

/// <summary>
///     BasilBot's account is a fixed record: nobody, whatever they hold, deletes it, restricts it, grants it
///     privileges or sets its password.
/// </summary>
public sealed class BasilBotProtectionTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task DeleteAsync_OfBasilBot_IsRefused()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPrivilegesExtensions.All));
		var bot = await app.SeedBotAsync();

		var result = await app.Users.Of(bot).ScopeTo(admin).DeleteAsync();

		Assert.Equal(UserOutcome.SystemAccount, result.Outcome);
		Assert.Null(bot.Value.DeletedAt);
	}

	[Fact]
	public async Task Silence_OfBasilBot_IsRefused()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPrivilegesExtensions.All));
		var bot = await app.SeedBotAsync();

		var silenced = await app.Users.Of(bot).ScopeTo(admin).Silence.ImposeAsync(
			app.Clock.GetUtcNow().AddHours(1));

		Assert.Equal(UserOutcome.SystemAccount, silenced.Outcome);
		Assert.Null(bot.Value.SilencedUntil);
	}

	[Fact]
	public async Task SetPrivileges_OfBasilBot_IsRefused()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPrivilegesExtensions.All));
		var bot = await app.SeedBotAsync();

		var result = await app.Users.Of(bot).ScopeTo(admin).Privileges.SetAsync(UserPrivileges.Player);

		Assert.Equal(UserOutcome.SystemAccount, result.Outcome);
		Assert.Equal(UserPrivileges.None, bot.Value.Privileges);
	}
}