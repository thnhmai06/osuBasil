using Basil.Application.Chat.Persistence;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Domain.Authentication;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Basil.Application.Servers.Tests.Objects;

/// <summary>
///     What the server recovers before it serves anyone: opens the configured channels, makes the account of the bot's
///     fixed id the bot and brings it online, and ends the matches left running by the previous run.
/// </summary>
public sealed class ServerRecoverTests
{
	[Fact]
	public async Task Recover_OpensStoredChannels()
	{
		using var app = new TestApplication();
		var channels = app.Provider.GetRequiredService<IChannelRepository>();
		var record = new GeneralChannelRecord
		{
			Name = "#stored",
			Value = new GeneralChannelData { AutoJoin = true }
		};
		channels.ListAsync(Arg.Any<CancellationToken>()).Returns([record]);

		var recovery = await app.Server.RecoverAsync(TestContext.Current.CancellationToken);

		Assert.NotNull(app.Channels.Find("#stored"));
		Assert.Equal(1, recovery.OpenedChannels);
	}

	[Fact]
	public async Task Recover_BotOnline()
	{
		using var app = new TestApplication();
		var bot = await app.SeedBotAsync();

		var recovery = await app.Server.RecoverAsync(TestContext.Current.CancellationToken);

		Assert.Contains(bot, app.Users.Online.Keys);
		Assert.True(recovery.BotOnline);
	}

	[Fact]
	public async Task OtherHoldsId_BotOnline()
	{
		using var app = new TestApplication();
		var other = await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret", UserPrivileges.Tournament);

		await app.Server.RecoverAsync(TestContext.Current.CancellationToken);

		var bot = Assert.Single(app.Users.Online.Keys, user => user.Id == BasilBot.Id);
		Assert.Equal(UserPrivileges.None, bot.Value.Privileges);
		Assert.False(await app.VerifiesPasswordAsync(other, "secret"));
	}

	[Fact]
	public async Task BotIdEmpty_BotCreated()
	{
		using var app = new TestApplication();

		await app.Server.RecoverAsync(TestContext.Current.CancellationToken);

		var bot = Assert.Single(app.Users.Online.Keys, user => user.Id == BasilBot.Id);
		Assert.Equal(BasilBot.DefaultName, bot.Value.Name);
	}

	[Fact]
	public async Task BotDeleted_BotRestored()
	{
		using var app = new TestApplication();
		await app.SeedAccountAsync(BasilBot.Id, "Gone", "secret", deletedAt: app.Clock.GetUtcNow());

		await app.Server.RecoverAsync(TestContext.Current.CancellationToken);

		var bot = Assert.Single(app.Users.Online.Keys, user => user.Id == BasilBot.Id);
		Assert.Null(bot.Value.DeletedAt);
	}

	[Fact]
	public async Task Recover_EndsRunningMatch()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("left running", "", RoomSource.Game, false);
		Assert.True(opened.IsOk);
		Assert.Null(room!.Match.Value.EndedAt);

		var recovery = await app.Server.RecoverAsync(TestContext.Current.CancellationToken);

		Assert.NotNull(room.Match.Value.EndedAt);
		Assert.Equal(room.Match, Assert.Single(app.LobbyEvents.Events.OfType<LobbyMatchRecovered>()).Match);
		Assert.Equal(1, recovery.ClosedMatches);
	}

	[Fact]
	public async Task ClosesLeftOpen_Counted()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Stopped", "password");
		var connection = await app.LoginAsync(user, SessionType.Stable);
		app.Clock.Advance(TimeSpan.FromSeconds(30));
		app.Users.MarkActive(connection);
		await app.Users.FlushActivityAsync();
		var lastActive = app.Clock.GetUtcNow();
		app.Clock.Advance(TimeSpan.FromHours(1));

		var recovery = await app.Server.RecoverAsync(TestContext.Current.CancellationToken);

		Assert.Equal(1, recovery.ClosedConnections);
		var entry = Assert.Single(app.StoredConnections);
		Assert.Equal(lastActive, entry.Value.ClosedAt);
		Assert.Equal(SessionEndReason.ServerStopped, entry.Value.CloseReason);
	}
}
