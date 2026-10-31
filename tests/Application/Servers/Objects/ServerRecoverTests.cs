using Basil.Application.Chat.Persistence;
using Basil.Application.Multiplayer.Objects.Events;
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
	public async Task RecoverAsync_OpensEveryStoredChannel()
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
	public async Task RecoverAsync_BringsBasilBotOnline()
	{
		using var app = new TestApplication();
		var bot = await app.SeedBotAsync();

		var recovery = await app.Server.RecoverAsync(TestContext.Current.CancellationToken);

		Assert.Contains(bot, app.Users.Online.Keys);
		Assert.True(recovery.BotOnline);
	}

	[Fact]
	public async Task RecoverAsync_AnotherAccountHoldsTheBotsId_TheBotComesOnlineInIt()
	{
		using var app = new TestApplication();
		var other = await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret", UserPermissions.RoomManageAny);

		await app.Server.RecoverAsync(TestContext.Current.CancellationToken);

		var bot = Assert.Single(app.Users.Online.Keys, user => user.Id == BasilBot.Id);
		Assert.Equal(UserPermissions.None, bot.Value.Permissions);
		Assert.False(await app.VerifiesPasswordAsync(other, "secret"));
	}

	[Fact]
	public async Task RecoverAsync_TheBotsIdHoldsNothing_TheBotIsCreatedAndOnline()
	{
		using var app = new TestApplication();

		await app.Server.RecoverAsync(TestContext.Current.CancellationToken);

		var bot = Assert.Single(app.Users.Online.Keys, user => user.Id == BasilBot.Id);
		Assert.Equal(BasilBot.DefaultName, bot.Value.Name);
	}

	[Fact]
	public async Task RecoverAsync_TheBotsAccountWasDeleted_TheBotIsRestoredAndOnline()
	{
		using var app = new TestApplication();
		await app.SeedAccountAsync(BasilBot.Id, "Gone", "secret", deletedAt: app.Clock.GetUtcNow());

		await app.Server.RecoverAsync(TestContext.Current.CancellationToken);

		var bot = Assert.Single(app.Users.Online.Keys, user => user.Id == BasilBot.Id);
		Assert.Null(bot.Value.DeletedAt);
	}

	[Fact]
	public async Task RecoverAsync_EndsTheMatchLeftRunning()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("left running", "", false, false,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(opened.IsOk);
		Assert.Null(room!.Match.Value.EndedAt);

		var recovery = await app.Server.RecoverAsync(TestContext.Current.CancellationToken);

		Assert.NotNull(room.Match.Value.EndedAt);
		Assert.Equal(room.Match, Assert.Single(app.LobbyEvents.Events.OfType<LobbyMatchRecovered>()).Match);
		Assert.Equal(1, recovery.ClosedMatches);
	}
}