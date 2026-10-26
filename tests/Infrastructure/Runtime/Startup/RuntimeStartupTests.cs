using Basil.Application.Persistence.Chat;
using Basil.Application.Persistence.Multiplayer;
using Basil.Testing;
using Basil.Domain.Chat;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Infrastructure.Runtime.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Basil.Infrastructure.Runtime.Tests.Startup;

/// <summary>
///     What the server does before it serves anyone: opens the configured channels, makes the account of the bot's
///     fixed id the bot and brings it online, and ends the matches left running by the previous run.
/// </summary>
public sealed class RuntimeStartupTests
{
	[Fact]
	public async Task StartAsync_OpensEveryStoredChannel()
	{
		using var app = new TestApplication();
		var channels = app.Provider.GetRequiredService<IChannelRepository>();
		var record = new GeneralChannelRecord
		{
			Name = "#stored",
			Value = new GeneralChannelData { AutoJoin = true }
		};
		channels.ListAsync(Arg.Any<CancellationToken>()).Returns([record]);

		await Startup(app, channels).StartAsync(TestContext.Current.CancellationToken);

		Assert.NotNull(app.Channels.Find("#stored"));
	}

	[Fact]
	public async Task StartAsync_BringsBasilBotOnline()
	{
		using var app = new TestApplication();
		var channels = app.Provider.GetRequiredService<IChannelRepository>();
		var bot = await app.SeedBotAsync();

		await Startup(app, channels).StartAsync(TestContext.Current.CancellationToken);

		Assert.Contains(bot, app.Users.Online.Keys);
	}

	[Fact]
	public async Task StartAsync_AnotherAccountHoldsTheBotsId_TheBotComesOnlineInIt()
	{
		using var app = new TestApplication();
		var channels = app.Provider.GetRequiredService<IChannelRepository>();
		var other = await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret", UserPermissions.RoomManageAny);

		await Startup(app, channels).StartAsync(TestContext.Current.CancellationToken);

		var bot = Assert.Single(app.Users.Online.Keys, user => user.Id == BasilBot.Id);
		Assert.Equal(UserPermissions.None, bot.Value.Permissions);
		Assert.False(await app.VerifiesPasswordAsync(other, "secret"));
	}

	[Fact]
	public async Task StartAsync_TheBotsIdHoldsNothing_TheBotIsCreatedAndOnline()
	{
		using var app = new TestApplication();
		var channels = app.Provider.GetRequiredService<IChannelRepository>();

		await Startup(app, channels).StartAsync(TestContext.Current.CancellationToken);

		var bot = Assert.Single(app.Users.Online.Keys, user => user.Id == BasilBot.Id);
		Assert.Equal(BasilBot.DefaultName, bot.Value.Name);
	}

	[Fact]
	public async Task StartAsync_TheBotsAccountWasDeleted_TheBotIsRestoredAndOnline()
	{
		using var app = new TestApplication();
		var channels = app.Provider.GetRequiredService<IChannelRepository>();
		await app.SeedAccountAsync(BasilBot.Id, "Gone", "secret", deletedAt: app.Clock.GetUtcNow());

		await Startup(app, channels).StartAsync(TestContext.Current.CancellationToken);

		var bot = Assert.Single(app.Users.Online.Keys, user => user.Id == BasilBot.Id);
		Assert.Null(bot.Value.DeletedAt);
	}

	[Fact]
	public async Task StartAsync_RunTwice_KeepsTheBotsConnectionOnline()
	{
		using var app = new TestApplication();
		var channels = app.Provider.GetRequiredService<IChannelRepository>();
		await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret");
		var startup = Startup(app, channels);
		await startup.StartAsync(TestContext.Current.CancellationToken);
		var connection = await app.Users.ConnectBotAsync(TestContext.Current.CancellationToken);

		await startup.StartAsync(TestContext.Current.CancellationToken);

		Assert.NotNull(connection);
		Assert.True(connection.IsOpen);
		Assert.Single(app.Users.Online.Keys, user => user.Id == BasilBot.Id);
	}

	[Fact]
	public async Task StartAsync_EndsTheMatchLeftRunning()
	{
		using var app = new TestApplication();
		var channels = app.Provider.GetRequiredService<IChannelRepository>();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("left running", "", false, false,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(opened.IsOk);
		Assert.Null(room!.Match.Value.EndedAt);

		await Startup(app, channels).StartAsync(TestContext.Current.CancellationToken);

		Assert.NotNull(room.Match.Value.EndedAt);
		var events = await app.Provider.GetRequiredService<IMatchEventRepository>()
			.ListAsync(room.Match, TestContext.Current.CancellationToken);
		Assert.Single(events, matchEvent => matchEvent.Type == MatchEventType.Closed);
	}

	private static RuntimeStartup Startup(TestApplication app, IChannelRepository channels)
	{
		return new RuntimeStartup(channels, app.Server, app.Users, app.Lobby,
			NullLogger<RuntimeStartup>.Instance);
	}
}
