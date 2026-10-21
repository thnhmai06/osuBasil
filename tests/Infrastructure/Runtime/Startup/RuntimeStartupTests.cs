using Basil.Application.Persistence.Chat;
using Basil.Application.Persistence.Multiplayer;
using Basil.Application.Runtime.Tests.Support;
using Basil.Domain.Chat;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Runtime.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Basil.Infrastructure.Runtime.Tests.Startup;

/// <summary>
///     What the server does before it serves anyone: opens the configured channels, brings BasilBot online when its
///     account exists, and ends the matches left running by the previous run.
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
