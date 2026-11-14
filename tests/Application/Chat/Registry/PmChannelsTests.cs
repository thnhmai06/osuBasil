using Basil.Application.Chat.Registry;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Chat.Tests.Registry;

/// <summary>What <see cref="PmChannels" /> keeps: one channel per online user and the connections in it.</summary>
public sealed class PmChannelsTests
{
	[Fact]
	public async Task OpenedNotRun_AddsConnection()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("PmOwner", "password");
		var connection = await app.LoginAsync(record, SessionType.Irc);
		Assert.Single(app.UserEvents.Events.OfType<UserConnectionOpened>());

		var channel = app.PmChannels.Of(app.Users.Of(record));

		Assert.Contains(connection, channel.Members);
	}

	[Fact]
	public async Task OpenNonTourney_AddsAll()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("ManyClients", "password");
		var irc = await app.LoginAsync(record, SessionType.Irc);
		var stable = await app.LoginAsync(record, SessionType.Stable);

		var channel = app.PmChannels.Of(app.Users.Of(record));

		Assert.Contains(irc, channel.Members);
		Assert.Contains(stable, channel.Members);
	}

	[Fact]
	public async Task TourneyConnection_NotAdded()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("Observer", "password",
			UserPrivilegesExtensions.Default | UserPrivileges.Tournament);
		var tourney = await app.LoginAsync(record, SessionType.StableTourney);

		var channel = app.PmChannels.Of(app.Users.Of(record));

		Assert.DoesNotContain(tourney, channel.Members);
	}

	[Fact]
	public async Task CalledAgain_ReturnsSameChannel()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("SameOwner", "password");
		await app.LoginAsync(record, SessionType.Irc);
		var owner = app.Users.Of(record);

		var first = app.PmChannels.Of(owner);
		var second = app.PmChannels.Of(owner);

		Assert.Same(first, second);
	}

	[Fact]
	public async Task NeverOnlineOwner_RegistersNothing()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("OfflineOwner", "password");
		var owner = app.Users.Of(record);

		var channel = app.PmChannels.Of(owner);

		Assert.NotNull(channel);
		Assert.Null(app.PmChannels.Find(owner));
	}

	[Fact]
	public async Task OfflineOwner_ForgetsChannel()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("LeftOwner", "password");
		var connection = await app.LoginAsync(record, SessionType.Irc);
		var owner = app.Users.Of(record);
		var channel = app.PmChannels.Of(owner);
		app.Users.Close(connection, SessionEndReason.LoggedOut);

		var returned = app.PmChannels.Of(owner);

		Assert.True(channel.IsClosed);
		Assert.True(returned.IsClosed);
		Assert.Null(app.PmChannels.Find(owner));
	}
}