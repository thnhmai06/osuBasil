using Basil.Application.Chat.Handlers;
using Basil.Application.Chat.Objects;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Auth;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Application.Chat.Tests.Handlers;

/// <summary>
///     What a connection does to its channels without the transport asking: it joins the channels that join
///     themselves when it opens and leaves its channels when it closes.
/// </summary>
public sealed class ConnectionChannelHandlerTests
{
	[Fact]
	public async Task Handle_UserConnectionOpened_JoinsTheChannelsThatJoinThemselves()
	{
		using var app = new TestApplication();
		var auto = await OpenChannelAsync(app, "#auto", true);
		var manual = await OpenChannelAsync(app, "#manual");
		var handler = Handler(app);
		var player = await RoomFixture.PlayerAsync(app, "Joiner");
		var opened = Assert.Single(app.UserEvents.Events.OfType<UserConnectionOpened>(),
			@event => @event.Connection == player);

		await handler.Handle(opened, TestContext.Current.CancellationToken);

		Assert.Contains(player, auto.Members);
		Assert.DoesNotContain(player, manual.Members);
	}

	[Fact]
	public async Task Handle_UserConnectionClosed_PartsItsChannels()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app, "#lobby");
		var handler = Handler(app);
		var player = await RoomFixture.PlayerAsync(app, "Chatter");
		Assert.True(channel.ScopeTo(player).Join().IsOk);
		app.Users.Close(player, SessionEndReason.ConnectionLost);
		var closed = Assert.Single(app.UserEvents.Events.OfType<UserConnectionClosed>(),
			@event => @event.Connection == player);

		await handler.Handle(closed, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(player, channel.Members);
	}

	private static ConnectionChannelHandler Handler(TestApplication app)
	{
		return new ConnectionChannelHandler(app.Channels, NullLogger<ConnectionChannelHandler>.Instance);
	}

	/// <summary>Opens the channel a standard account makes for itself, with no read or write role required.</summary>
	/// <param name="app">The application the channel lives in.</param>
	/// <param name="name">The channel name, with its leading <c>#</c>.</param>
	/// <param name="autoJoin">Whether a connection joins the channel at login.</param>
	/// <returns>The open channel.</returns>
	private static async Task<GeneralChannel> OpenChannelAsync(TestApplication app, string name, bool autoJoin = false)
	{
		var admin = await app.CreateUserAsync($"Admin{name.TrimStart('#')}", "password", UserPermissionsGroups.All);
		var by = await app.LoginAsync(admin);
		var record = new GeneralChannelRecord
		{
			Name = name,
			Value = new GeneralChannelData { AutoJoin = autoJoin }
		};
		Assert.True((await app.Server.ScopeTo(by).Channels.CreateAsync(record,
			TestContext.Current.CancellationToken)).IsOk);
		return app.Channels.Find(name)!;
	}
}