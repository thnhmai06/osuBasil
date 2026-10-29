using Basil.Application.Auth.Persistence;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Auth;
using Basil.Domain.Moderation;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Users.Tests.Users;

/// <summary>
///     Ended sessions are deleted once they are older than <c>Sessions.RetentionPeriod</c> (90 days by default); active
///     sessions, the marks that make a device enter its password again and the moderation log stay.
/// </summary>
public sealed class SessionRetentionTests
{
	private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(90);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task AnEndedSessionOlderThanTheRetentionIsDeleted()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("OldEnded", "password");
		var connection = await app.LoginAsync(user);
		app.Users.Close(connection, SessionEndReason.LoggedOut);
		app.Clock.Advance(DefaultRetention + TimeSpan.FromDays(1));

		var deleted = await app.Users.PurgeEndedSessionsAsync(Token);

		Assert.Equal(1, deleted);
		Assert.DoesNotContain(app.SessionEntries, entry => entry.Id == connection.Session.Id);
	}

	[Fact]
	public async Task AnEndedSessionYoungerThanTheRetentionStays()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("YoungEnded", "password");
		var connection = await app.LoginAsync(user);
		app.Users.Close(connection, SessionEndReason.LoggedOut);
		app.Clock.Advance(DefaultRetention - TimeSpan.FromDays(1));

		var deleted = await app.Users.PurgeEndedSessionsAsync(Token);

		Assert.Equal(0, deleted);
		Assert.Contains(app.SessionEntries, entry => entry.Id == connection.Session.Id);
	}

	[Fact]
	public async Task AnActiveSessionStaysHoweverOldItIs()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(new ServerSettings
		{
			Sessions = new ServerSettings.SessionSettings
			{
				ApiToken = new ServerSettings.ApiTokenSettings { Lifetime = TimeSpan.FromDays(1000) }
			}
		});
		var user = await app.CreateUserAsync("Active", "password");
		var connection = await app.LoginAsync(user);
		app.Clock.Advance(DefaultRetention * 2);

		var deleted = await app.Users.PurgeEndedSessionsAsync(Token);

		Assert.Equal(0, deleted);
		Assert.Contains(app.SessionEntries, entry => entry.Id == connection.Session.Id);
	}

	[Fact]
	public async Task TheRetentionIsReadFromTheServerSettings()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(new ServerSettings
		{
			Sessions = new ServerSettings.SessionSettings { RetentionPeriod = TimeSpan.FromDays(7) }
		});
		var user = await app.CreateUserAsync("ShortRetention", "password");
		var connection = await app.LoginAsync(user);
		app.Users.Close(connection, SessionEndReason.LoggedOut);
		app.Clock.Advance(TimeSpan.FromDays(8));

		Assert.Equal(1, await app.Users.PurgeEndedSessionsAsync(Token));
	}

	[Fact]
	public async Task ASessionThatMarksADeviceForReauthenticationStays()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Marked", "password");
		var connection = await app.LoginAsync(user, Basil.Domain.Auth.ConnectionType.Osu);
		await app.Provider.GetRequiredService<ISessionRepository>().EndAsync(user, null, null,
			[Basil.Domain.Auth.ConnectionType.Osu], app.Clock.GetUtcNow(), SessionEndReason.Revoked, true, Token);
		app.Clock.Advance(DefaultRetention + TimeSpan.FromDays(1));

		var deleted = await app.Users.PurgeEndedSessionsAsync(Token);

		Assert.Equal(0, deleted);
		Assert.Contains(app.SessionEntries, entry => entry.Id == connection.Session.Id);
	}

	[Fact]
	public async Task TheModerationLogIsNeverPurged()
	{
		using var app = new TestApplication();
		var staff = await app.CreateUserAsync("Staff", "password");
		var target = await app.CreateUserAsync("Target", "password");
		var log = app.Provider.GetRequiredService<IModerationLogRepository>();
		await log.AppendAsync(new ModerationData { Action = ModerationAction.Note, By = staff, Target = target, Timestamp = app.Clock.GetUtcNow() }, Token);
		var connection = await app.LoginAsync(target);
		app.Users.Close(connection, SessionEndReason.LoggedOut);
		app.Clock.Advance(DefaultRetention * 10);

		await app.Users.PurgeEndedSessionsAsync(Token);

		Assert.Single(app.ModerationEntries);
	}
}
