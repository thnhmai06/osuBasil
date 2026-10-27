using Basil.Application.Chat.Runtime;
using Basil.Application.Users.Connections;
using Basil.Application.Users.Users;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Chat.Tests.Runtime;

/// <summary>What <see cref="PmChannels" /> keeps: one channel per online user and the connections in it.</summary>
public sealed class PmChannelsTests
{
	[Fact]
	public async Task Of_WhenTheConnectionOpenedAndItsHandlerHasNotRun_AddsTheConnection()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("PmOwner", "password");
		var connection = await app.LoginAsync(record, ConnectionType.Irc);
		Assert.Single(app.UserEvents.Events.OfType<UserConnectionOpened>());

		var channel = app.PmChannels.Of(app.Users.Of(record));

		Assert.Contains(connection, channel.Members);
	}

	[Fact]
	public async Task Of_ForEveryOpenNonTourneyConnection_AddsAllOfThem()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("ManyClients", "password");
		var irc = await app.LoginAsync(record, ConnectionType.Irc);
		var bancho = await app.LoginAsync(record, ConnectionType.Bancho);

		var channel = app.PmChannels.Of(app.Users.Of(record));

		Assert.Contains(irc, channel.Members);
		Assert.Contains(bancho, channel.Members);
	}

	[Fact]
	public async Task Of_ForATourneyConnection_DoesNotAddIt()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("Observer", "password",
			UserPermissionsGroups.Default | UserPermissions.RoomObserve);
		var tourney = await app.LoginAsync(record, ConnectionType.Tourney);

		var channel = app.PmChannels.Of(app.Users.Of(record));

		Assert.DoesNotContain(tourney, channel.Members);
	}

	[Fact]
	public async Task Of_CalledAgain_ReturnsTheSameChannel()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("SameOwner", "password");
		await app.LoginAsync(record, ConnectionType.Irc);
		var owner = app.Users.Of(record);

		var first = app.PmChannels.Of(owner);
		var second = app.PmChannels.Of(owner);

		Assert.Same(first, second);
	}

	[Fact]
	public async Task Of_ForAnOwnerWhoWasNeverOnline_RegistersNothing()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("OfflineOwner", "password");
		var owner = app.Users.Of(record);

		var channel = app.PmChannels.Of(owner);

		Assert.NotNull(channel);
		Assert.Null(app.PmChannels.Find(owner));
	}

	[Fact]
	public async Task Of_ForAnOwnerWhoWentOffline_ForgetsTheChannel()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("LeftOwner", "password");
		var connection = await app.LoginAsync(record, ConnectionType.Irc);
		var owner = app.Users.Of(record);
		var channel = app.PmChannels.Of(owner);
		app.Users.Close(connection, ConnectionCloseReason.LoggedOut);

		var returned = app.PmChannels.Of(owner);

		Assert.True(channel.IsClosed);
		Assert.True(returned.IsClosed);
		Assert.Null(app.PmChannels.Find(owner));
	}
}