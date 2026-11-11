using System.Diagnostics.Metrics;
using System.Net;
using Basil.Application.Common.Settings;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Registry;

/// <summary>
///     One user holds at most <c>Sessions.MaxPerUser</c> open connections of each kind (Stable 1, StableTourney 10,
///     Irc 5, Api 10 by default); idle ones are closed before a login is refused and an HTTP API connection replaces the
///     oldest.
/// </summary>
public sealed class SessionLimitTests
{
	private static readonly UserPrivileges Observer = UserPrivilegesExtensions.Default | UserPrivileges.Tournament;

	private static ServerSettings Limits(int stable = 1, int tourney = 10, int irc = 5, int api = 10)
	{
		return new ServerSettings
		{
			Sessions = new ServerSettings.SessionSettings
			{
				MaxPerUser = new ServerSettings.MaxSessionsSettings
					{ Stable = stable, StableTourney = tourney, Irc = irc, Api = api }
			}
		};
	}

	[Fact]
	public void DefaultLimits_AreStable()
	{
		var max = new ServerSettings().Sessions.MaxPerUser;

		Assert.Equal([1, 10, 5, 10],
		[
			max.For(SessionType.Stable), max.For(SessionType.StableTourney), max.For(SessionType.Irc),
			max.For(SessionType.Api)
		]);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void NonPositiveLimit_Refused(int value)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new ServerSettings.MaxSessionsSettings { Stable = value });
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new ServerSettings.MaxSessionsSettings { StableTourney = value });
		Assert.Throws<ArgumentOutOfRangeException>(() => new ServerSettings.MaxSessionsSettings { Irc = value });
		Assert.Throws<ArgumentOutOfRangeException>(() => new ServerSettings.MaxSessionsSettings { Api = value });
	}

	[Theory]
	[InlineData(SessionType.Stable, 1)]
	[InlineData(SessionType.StableTourney, 3)]
	[InlineData(SessionType.Irc, 2)]
	public async Task AtLimit_ReturnsTooMany(SessionType type, int limit)
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(Limits(1, 3, 2));
		var user = await app.CreateUserAsync("AtLimit", "password", Observer);
		for (var i = 0; i < limit; i++) await app.LoginAsync(user, type);

		var result = await app.AttemptLoginAsync(user.Value.Name, "password", type);

		Assert.Equal(LoginFailure.TooManySessions, result.Failure);
		Assert.Equal(limit, app.Users.Of(user)[type].Count);
	}

	[Theory]
	[InlineData(SessionType.Stable)]
	[InlineData(SessionType.StableTourney)]
	[InlineData(SessionType.Irc)]
	public async Task BelowLimit_Succeeds(SessionType type)
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(Limits(2, 2, 2));
		var user = await app.CreateUserAsync("BelowLimit", "password", Observer);
		await app.LoginAsync(user, type);

		var result = await app.AttemptLoginAsync(user.Value.Name, "password", type);

		Assert.NotNull(result.Connection);
	}

	[Fact]
	public async Task LimitPerKind_OtherKindUnaffected()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("PerKind", "password", Observer);
		await app.LoginAsync(user, SessionType.Stable);

		var tourney = await app.AttemptLoginAsync(user.Value.Name, "password", SessionType.StableTourney);
		var irc = await app.AttemptLoginAsync(user.Value.Name, "password", SessionType.Irc);
		var api = await app.AttemptLoginAsync(user.Value.Name, "password");

		Assert.All([tourney, irc, api], result => Assert.NotNull(result.Connection));
	}

	[Fact]
	public async Task LimitPerUser_OtherUserUnaffected()
	{
		using var app = new TestApplication();
		var first = await app.CreateUserAsync("FullUser", "password");
		var second = await app.CreateUserAsync("FreeUser", "password");
		await app.LoginAsync(first, SessionType.Stable);

		var result = await app.AttemptLoginAsync(second.Value.Name, "password", SessionType.Stable);

		Assert.NotNull(result.Connection);
	}

	[Fact]
	public async Task RaisedLimit_AdmitsNext()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Raised", "password");
		await app.LoginAsync(user, SessionType.Stable);
		await app.SetSettingsAsync(Limits(2));

		var result = await app.AttemptLoginAsync(user.Value.Name, "password", SessionType.Stable);

		Assert.NotNull(result.Connection);
	}

	[Theory]
	[InlineData(SessionType.Stable)]
	[InlineData(SessionType.Irc)]
	public async Task IdleAtLimit_ClosesAdmitsNew(SessionType type)
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(Limits(1, 1, 1));
		var user = await app.CreateUserAsync("Idle", "password");
		var idle = await app.LoginAsync(user, type);
		app.Clock.Advance(TimeSpan.FromSeconds(301));

		var result = await app.AttemptLoginAsync(user.Value.Name, "password", type);

		Assert.NotNull(result.Connection);
		Assert.False(idle.IsOpen);
		Assert.Equal([result.Connection], app.Users.Of(user)[type]);
	}

	[Fact]
	public async Task IdleConnections_ClosesOwnKind()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("IdleElsewhere", "password");
		await app.LoginAsync(user, SessionType.Stable);
		var api = await app.LoginAsync(user);
		app.Clock.Advance(TimeSpan.FromSeconds(301));

		var result = await app.AttemptLoginAsync(user.Value.Name, "password", SessionType.Stable);

		Assert.NotNull(result.Connection);
		Assert.True(api.IsOpen);
	}

	[Fact]
	public async Task ApiAtLimit_ReplacesOldest()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(Limits(api: 2));
		var user = await app.CreateUserAsync("ApiOldest", "password");
		var oldest = await app.LoginAsync(user);
		app.Clock.Advance(TimeSpan.FromSeconds(10));
		var newer = await app.LoginAsync(user);
		app.Clock.Advance(TimeSpan.FromSeconds(10));

		var result = await app.AttemptLoginAsync(user.Value.Name, "password");

		Assert.NotNull(result.Connection);
		Assert.False(oldest.IsOpen);
		Assert.True(newer.IsOpen);
		Assert.Equal(2, app.Users.Of(user)[SessionType.Api].Count);
	}

	[Fact]
	public async Task ReplacedApiSession_TokenRefused()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(Limits(api: 1));
		var user = await app.CreateUserAsync("ApiEvicted", "password");
		var first = await app.LoginAsync(user);
		var evictedAt = app.Clock.GetUtcNow();

		await app.AttemptLoginAsync(user.Value.Name, "password");
		var reused = await app.Users.LoginWithTokenAsync(SessionType.Api, first.Token, IPAddress.Loopback,
			ct: TestContext.Current.CancellationToken);

		Assert.Equal(new SessionEnd(evictedAt, SessionEndReason.Replaced), first.End);
		Assert.Null(reused.Connection);
	}

	[Fact]
	public async Task ApiLimitOne_NeverRefuses()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(Limits(api: 1));
		var user = await app.CreateUserAsync("ApiOne", "password");
		var first = await app.LoginAsync(user);

		var second = await app.AttemptLoginAsync(user.Value.Name, "password");

		Assert.NotNull(second.Connection);
		Assert.False(first.IsOpen);
	}

	[Fact]
	public async Task LastSlotRace_OneAccepted()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Racer", "password");

		var results = await Task.WhenAll(Enumerable.Range(0, 8)
			.Select(_ => Task.Run(() => app.AttemptLoginAsync(user.Value.Name, "password", SessionType.Stable),
				TestContext.Current.CancellationToken)));

		Assert.Equal(1, results.Count(result => result.Connection is not null));
		Assert.Equal(7, results.Count(result => result.Failure is LoginFailure.TooManySessions));
		Assert.Single(app.Users.Of(user)[SessionType.Stable]);
	}

	[Fact]
	public async Task RefusedLogin_CountedWithReason()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Counted", "password");
		await app.LoginAsync(user, SessionType.Stable);
		var seen = new List<(long Value, string? Type, string? Reason)>();
		using var listener = new MeterListener();
		listener.InstrumentPublished = (instrument, meterListener) =>
		{
			if (instrument.Name == ConnectionMetrics.RejectedName) meterListener.EnableMeasurementEvents(instrument);
		};
		listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
		{
			string? type = null, reason = null;
			foreach (var tag in tags)
			{
				if (tag.Key == ConnectionMetrics.TypeTag) type = tag.Value as string;
				if (tag.Key == ConnectionMetrics.ReasonTag) reason = tag.Value as string;
			}

			lock (seen)
			{
				seen.Add((value, type, reason));
			}
		});
		listener.Start();

		await app.AttemptLoginAsync(user.Value.Name, "password", SessionType.Stable);

		lock (seen)
		{
			Assert.Contains((1L, nameof(SessionType.Stable), ConnectionMetrics.TooManySessions), seen);
		}
	}

	[Fact]
	public async Task BotAfterIdle_KeepsConnections()
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();
		var bot = await app.Users.ConnectBotAsync(TestContext.Current.CancellationToken);
		app.Clock.Advance(TimeSpan.FromHours(1));

		var again = await app.Users.ConnectBotAsync(TestContext.Current.CancellationToken);

		Assert.Same(bot, again);
		Assert.All(bot!.Connections, connection => Assert.True(connection.IsOpen));
	}
}