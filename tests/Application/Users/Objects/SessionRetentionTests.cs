using Basil.Application.Shared.Settings;
using Basil.Application.Users.Persistence;
using Basil.Domain.Authentication;
using Basil.Domain.Moderation;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Users.Tests.Objects;

/// <summary>
///     Ended sessions are deleted once they are older than <c>Sessions.RetentionPeriod</c> (90 days by default); active
///     sessions and the moderation log stay.
/// </summary>
public sealed class SessionRetentionTests
{
	private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(90);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task EndedOlder_Deleted()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("OldEnded", "password");
		var connection = await app.LoginAsync(user);
		app.Users.Close(connection, SessionEndReason.LoggedOut);
		app.Clock.Advance(DefaultRetention + TimeSpan.FromDays(1));

		var deleted = await app.Users.PurgeEndedSessionsAsync();

		Assert.Equal(1, deleted);
		Assert.DoesNotContain(app.StoredSessions, entry => entry.Id == connection.Session.Id);
	}

	[Fact]
	public async Task EndedYounger_Stays()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("YoungEnded", "password");
		var connection = await app.LoginAsync(user);
		app.Users.Close(connection, SessionEndReason.LoggedOut);
		app.Clock.Advance(DefaultRetention - TimeSpan.FromDays(1));

		var deleted = await app.Users.PurgeEndedSessionsAsync();

		Assert.Equal(0, deleted);
		Assert.Contains(app.StoredSessions, entry => entry.Id == connection.Session.Id);
	}

	[Fact]
	public async Task ActiveSession_StaysHoweverOld()
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

		var deleted = await app.Users.PurgeEndedSessionsAsync();

		Assert.Equal(0, deleted);
		Assert.Contains(app.StoredSessions, entry => entry.Id == connection.Session.Id);
	}

	[Fact]
	public async Task Retention_ReadFromSettings()
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

		Assert.Equal(1, await app.Users.PurgeEndedSessionsAsync());
	}

	[Fact]
	public async Task ModerationLog_NeverPurged()
	{
		using var app = new TestApplication();
		var staff = await app.CreateUserAsync("Staff", "password");
		var target = await app.CreateUserAsync("Target", "password");
		var log = app.Provider.GetRequiredService<IModerationLogRepository>();
		await log.AppendAsync(
			new ModerationData
				{ Action = ModerationAction.Note, Actor = staff, Target = target, Timestamp = app.Clock.GetUtcNow() });
		var connection = await app.LoginAsync(target);
		app.Users.Close(connection, SessionEndReason.LoggedOut);
		app.Clock.Advance(DefaultRetention * 10);

		await app.Users.PurgeEndedSessionsAsync();

		Assert.Single(app.ModerationEntries);
	}
}