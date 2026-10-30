using System.Net;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Auth.Persistence;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Connections;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Users.Tests.Users;

/// <summary>
///     The HTTP API token: 256 random bits, stored only as a SHA-256 hash, renewed by use for the lifetime of the
///     server settings, verified against the stored sessions the first time, then held by an open connection until it
///     has been idle for 300 seconds.
/// </summary>
public sealed class ApiTokenTests
{
	private static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(7);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static Task<LoginResult> OpenAsync(TestApplication app, string token,
		SessionType type = SessionType.Api)
	{
		return app.Users.LoginWithTokenAsync(type, token, IPAddress.Loopback, cancellationToken: Token);
	}

	[Fact]
	public async Task Login_IssuesATokenOf256RandomBitsThatIsNotRepeated()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("TokenUser", "password");

		var first = await app.LoginAsync(user);
		var second = await app.LoginAsync(user);

		Assert.Equal(32, Convert.FromBase64String(first.Token.Replace('-', '+').Replace('_', '/') + "=").Length);
		Assert.NotEqual(first.Token, second.Token);
	}

	[Fact]
	public async Task Login_StoresOnlyTheSha256HashOfTheToken()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("HashUser", "password");
		var connection = await app.LoginAsync(user);
		var sessions = app.Provider.GetRequiredService<ISessionRepository>();

		var byHash = await sessions.FindByTokenAsync(SessionType.Api,
			SHA256.HashData(Encoding.UTF8.GetBytes(connection.Token)), app.Clock.GetUtcNow(), Token);
		var byPlainToken = await sessions.FindByTokenAsync(SessionType.Api,
			Encoding.UTF8.GetBytes(connection.Token), app.Clock.GetUtcNow(), Token);

		Assert.Equal(connection.Session.Id, byHash!.Id);
		Assert.Null(byPlainToken);
	}

	[Fact]
	public async Task Login_ExpiresTheTokenAfterTheLifetimeOfTheServerSettings()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("DefaultLifetime", "password");

		var connection = await app.LoginAsync(user);

		Assert.Equal(app.Clock.GetUtcNow() + DefaultLifetime, connection.Session.ExpiresAt);
	}

	[Fact]
	public async Task Login_UsesTheConfiguredLifetime()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(new ServerSettings
		{
			Sessions = new ServerSettings.SessionSettings
			{
				ApiToken = new ServerSettings.ApiTokenSettings { Lifetime = TimeSpan.FromHours(2) }
			}
		});
		var user = await app.CreateUserAsync("ShortLifetime", "password");

		var connection = await app.LoginAsync(user);

		Assert.Equal(app.Clock.GetUtcNow() + TimeSpan.FromHours(2), connection.Session.ExpiresAt);
	}

	[Fact]
	public async Task Use_RenewsTheExpiryFromTheMomentOfUse_AndStoresItOnTheNextFlush()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Sliding", "password");
		var connection = await app.LoginAsync(user);
		var issued = connection.Session.ExpiresAt;

		app.Clock.Advance(TimeSpan.FromDays(3));
		app.Users.MarkActive(connection);

		Assert.Equal(app.Clock.GetUtcNow() + DefaultLifetime, connection.Session.ExpiresAt);
		Assert.Equal(issued, app.TokenExpiryOf(connection.Session.Id));

		await app.Users.FlushActivityAsync(Token);

		Assert.Equal(app.Clock.GetUtcNow() + DefaultLifetime, app.TokenExpiryOf(connection.Session.Id));
	}

	[Fact]
	public async Task Token_OfAnOpenConnection_OpensThatSameConnection()
	{
		using var app = new TestApplication();
		var connection = await app.LoginAsync(await app.CreateUserAsync("OpenToken", "password"));

		var result = await OpenAsync(app, connection.Token);

		Assert.Same(connection, result.Connection);
	}

	[Fact]
	public async Task Token_OfAConnectionSweptAsIdle_IsVerifiedAgainAndOpensANewConnectionOfTheSameSession()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Evicted", "password");
		var first = await app.LoginAsync(user);
		app.Clock.Advance(TimeSpan.FromSeconds(301));
		Assert.Equal(1, app.Users.CloseIdle());
		Assert.Null(app.Users.Find(first.Token));
		Assert.False(app.Users.Online.ContainsKey(user));

		var result = await OpenAsync(app, first.Token);

		Assert.True(result.Succeeded);
		Assert.NotSame(first, result.Connection);
		Assert.Equal(first.Session.Id, result.Connection!.Session.Id);
		Assert.Same(result.Connection, app.Users.Find(first.Token));
		Assert.True(app.Users.Online.ContainsKey(user));
	}

	[Fact]
	public async Task Token_OfAConnectionSweptAsIdle_StaysValidForTheSession()
	{
		using var app = new TestApplication();
		var first = await app.LoginAsync(await app.CreateUserAsync("StillValid", "password"));
		app.Clock.Advance(TimeSpan.FromSeconds(301));
		app.Users.CloseIdle();

		Assert.Null(app.SessionEntries.Single(entry => entry.Id == first.Session.Id).EndedAt);
	}

	[Fact]
	public async Task Token_PastItsExpiry_OpensNothing()
	{
		using var app = new TestApplication();
		var first = await app.LoginAsync(await app.CreateUserAsync("Expired", "password"));
		app.Clock.Advance(TimeSpan.FromSeconds(301));
		app.Users.CloseIdle();
		app.Clock.Advance(DefaultLifetime);

		var result = await OpenAsync(app, first.Token);

		Assert.Equal(LoginFailure.InvalidToken, result.Failure);
	}

	[Fact]
	public async Task Token_OfAnOpenConnectionPastItsExpiry_OpensNothingAndClosesTheConnection()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(new ServerSettings
		{
			Sessions = new ServerSettings.SessionSettings
			{
				ApiToken = new ServerSettings.ApiTokenSettings { Lifetime = TimeSpan.FromSeconds(60) }
			}
		});
		var connection = await app.LoginAsync(await app.CreateUserAsync("ShortToken", "password"));
		app.Clock.Advance(TimeSpan.FromSeconds(61));

		var result = await OpenAsync(app, connection.Token);

		Assert.Equal(LoginFailure.InvalidToken, result.Failure);
		Assert.False(connection.IsOpen);
	}

	[Fact]
	public async Task Token_ThatMatchesNoSession_OpensNothing()
	{
		using var app = new TestApplication();

		var result = await OpenAsync(app, "not-a-token");

		Assert.Equal(LoginFailure.InvalidToken, result.Failure);
	}

	[Fact]
	public async Task Token_OfADeletedAccount_OpensNothing()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));
		var user = await app.CreateUserAsync("Doomed", "password");
		var connection = await app.LoginAsync(user);
		app.Clock.Advance(TimeSpan.FromSeconds(301));
		app.Users.CloseIdle();
		Assert.True((await app.Users.Of(user).ScopeTo(admin).DeleteAsync(Token)).IsOk);

		var result = await OpenAsync(app, connection.Token);

		Assert.False(result.Succeeded);
	}

	[Fact]
	public async Task AnApiConnection_ThatSentNothingFor300Seconds_IsClosed_AndOneThatSpokeIsNot()
	{
		using var app = new TestApplication();
		var quiet = await app.LoginAsync(await app.CreateUserAsync("Quiet", "password"));
		var active = await app.LoginAsync(await app.CreateUserAsync("Active", "password"));
		app.Clock.Advance(TimeSpan.FromSeconds(299));
		app.Users.MarkActive(active);
		app.Clock.Advance(TimeSpan.FromSeconds(2));

		app.Users.CloseIdle();

		Assert.False(quiet.IsOpen);
		Assert.True(active.IsOpen);
	}

	[Fact]
	public async Task Login_WithAnIrcKey_IsRefusedForAnApiToken()
	{
		using var app = new TestApplication();
		var connection = await app.LoginAsync(await app.CreateUserAsync("Mixed", "password"));

		var result = await OpenAsync(app, connection.Token, SessionType.Irc);

		Assert.Equal(LoginFailure.InvalidToken, result.Failure);
	}

	[Fact]
	public async Task LoginWithToken_OfTheOtherKinds_Throws()
	{
		using var app = new TestApplication();

		await Assert.ThrowsAsync<ArgumentException>(() => OpenAsync(app, "token", SessionType.Osu));
	}
}
