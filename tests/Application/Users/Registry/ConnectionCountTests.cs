using Basil.Application.Shared.Settings;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Registry;

/// <summary>
///     The connection counters: a connection that opens, is refused or closes is counted once for its kind, and the
///     bot's own connections are never counted. The counters are global, so every assertion is on a change, and the
///     project runs its tests one at a time (<c>AssemblyInfo.cs</c>).
/// </summary>
public sealed class ConnectionCountTests
{
	private static long Total(SessionType type, ConnectionCount kind)
	{
		return ConnectionMetrics.Total(type, kind);
	}

	[Fact]
	public async Task Login_CountsOpened()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("CountOpen", "password", UserPrivilegesExtensions.Default);
		var before = Total(SessionType.Api, ConnectionCount.Opened);

		await app.LoginAsync(user);

		Assert.Equal(before + 1, Total(SessionType.Api, ConnectionCount.Opened));
	}

	[Fact]
	public async Task BotConnections_NotCounted()
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();
		var before = (Total(SessionType.Stable, ConnectionCount.Opened), Total(SessionType.Irc, ConnectionCount.Opened));

		await app.Users.ConnectBotAsync(TestContext.Current.CancellationToken);

		Assert.Equal(before, (Total(SessionType.Stable, ConnectionCount.Opened), Total(SessionType.Irc, ConnectionCount.Opened)));
	}

	[Fact]
	public async Task LoginOverLimit_CountsRejected()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(new ServerSettings
		{
			Sessions = new ServerSettings.SessionSettings
			{
				MaxPerUser = new ServerSettings.MaxSessionsSettings { Stable = 1 }
			}
		});
		var user = await app.CreateUserAsync("CountReject", "password", UserPrivilegesExtensions.Default);
		await app.LoginAsync(user, SessionType.Stable);
		var before = Total(SessionType.Stable, ConnectionCount.Rejected);

		var result = await app.AttemptLoginAsync(user.Value.Name, "password", SessionType.Stable);

		Assert.Equal(LoginFailure.TooManySessions, result.Failure);
		Assert.Equal(before + 1, Total(SessionType.Stable, ConnectionCount.Rejected));
	}

	[Fact]
	public async Task Close_CountsClosedOnly()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("CountClose", "password", UserPrivilegesExtensions.Default);
		var connection = await app.LoginAsync(user);
		var before = Snapshot();

		app.Users.Close(connection, SessionEndReason.Revoked);

		Assert.Equal((before.Closed + 1, before.Timeout, before.Errors), Snapshot());
	}

	[Fact]
	public async Task CloseTimedOut_CountsClosedAndTimeout()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("CountTimeout", "password", UserPrivilegesExtensions.Default);
		var connection = await app.LoginAsync(user);
		var before = Snapshot();

		app.Users.Close(connection, SessionEndReason.TimedOut);

		Assert.Equal((before.Closed + 1, before.Timeout + 1, before.Errors), Snapshot());
	}

	[Fact]
	public async Task CloseConnectionLost_CountsClosedAndErrors()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("CountLost", "password", UserPrivilegesExtensions.Default);
		var connection = await app.LoginAsync(user);
		var before = Snapshot();

		app.Users.Close(connection, SessionEndReason.ConnectionLost);

		Assert.Equal((before.Closed + 1, before.Timeout, before.Errors + 1), Snapshot());
	}

	[Fact]
	public async Task CloseTwice_CountedOnce()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("CountTwice", "password", UserPrivilegesExtensions.Default);
		var connection = await app.LoginAsync(user);
		app.Users.Close(connection, SessionEndReason.ConnectionLost);
		var before = Snapshot();

		app.Users.Close(connection, SessionEndReason.ConnectionLost);

		Assert.Equal(before, Snapshot());
	}

	private static (long Closed, long Timeout, long Errors) Snapshot()
	{
		return (Total(SessionType.Api, ConnectionCount.Closed), Total(SessionType.Api, ConnectionCount.Timeout),
			Total(SessionType.Api, ConnectionCount.Errors));
	}
}
