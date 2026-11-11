using Basil.Application.Chat.Objects;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Authentication;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Infrastructure.Chat.Handlers;
using Basil.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Chat.Tests.Handlers;

/// <summary>
///     What a connection does to its channels without the transport asking: it joins the channels that join
///     themselves when it opens and leaves its channels when it closes.
/// </summary>
public sealed class ConnectionChannelHandlerTests
{
	[Fact]
	public async Task ConnectionOpened_JoinsAutoJoin()
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
	public async Task ConnectionClosed_PartsChannels()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app, "#lobby");
		var handler = Handler(app);
		var player = await RoomFixture.PlayerAsync(app, "Chatter");
		Assert.True(channel.ScopeTo(player).Join() == ChannelOutcome.Ok);
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
		var admin = await app.CreateUserAsync($"Admin{name.TrimStart('#')}", "password", UserPrivilegesExtensions.All);
		var actor = await app.LoginAsync(admin);
		var record = new GeneralChannelRecord
		{
			Name = name,
			Value = new GeneralChannelData { AutoJoin = autoJoin }
		};
		Assert.True((await app.Server.ScopeTo(actor).Channels.CreateAsync(record)) == ServerOutcome.Ok);
		return app.Channels.Find(name)!;
	}
}