using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Objects;

/// <summary>
///     BasilBot's account is a fixed record: nobody, whatever they hold, deletes it, restricts it, grants it
///     permissions or sets its password.
/// </summary>
public sealed class BasilBotProtectionTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task DeleteAsync_OfBasilBot_IsRefused()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPermissionsGroups.All));
		var bot = await app.SeedBotAsync();

		var result = await app.Users.Of(bot).ScopeTo(admin).DeleteAsync(Token);

		Assert.Equal(UserOutcome.SystemAccount, result.Outcome);
		Assert.Null(bot.Value.DeletedAt);
	}

	[Fact]
	public async Task RestrictAndSilence_OfBasilBot_AreRefused()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPermissionsGroups.All));
		var bot = await app.SeedBotAsync();
		var restrictions = app.Users.Of(bot).ScopeTo(admin).Restrictions;

		var (restricted, restriction) = await restrictions.RestrictAsync(UserPermissions.ChatPost, null,
			cancellationToken: Token);
		var (silenced, _) = await restrictions.SilenceAsync(app.Clock.GetUtcNow().AddHours(1),
			cancellationToken: Token);

		Assert.Equal(UserOutcome.SystemAccount, restricted.Outcome);
		Assert.Equal(UserOutcome.SystemAccount, silenced.Outcome);
		Assert.Null(restriction);
	}

	[Fact]
	public async Task SetPermissions_OfBasilBot_IsRefused()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPermissionsGroups.All));
		var bot = await app.SeedBotAsync();

		var result = await app.Users.Of(bot).ScopeTo(admin).Permissions.SetAsync(UserPermissions.ChatPost, Token);

		Assert.Equal(UserOutcome.SystemAccount, result.Outcome);
		Assert.Equal(UserPermissions.None, bot.Value.Permissions);
	}
}
