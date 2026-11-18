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

	private static async Task<GeneralChannel> OpenChannelAsync(TestApplication app, string name, bool autoJoin = false)
	{
		var admin = await app.CreateUserAsync($"Admin{name.TrimStart('#')}", "password", UserPrivilegesExtensions.All);
		var actor = await app.LoginAsync(admin);
		var record = new GeneralChannelRecord
		{
			Name = name,
			Value = new GeneralChannelData { AutoJoin = autoJoin }
		};
		Assert.True(await app.Server.ScopeTo(actor).Channels.CreateAsync(record) == ServerOutcome.Ok);
		return app.Channels.Find(name)!;
	}
}