using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Chat.Registry;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Infrastructure.Chat.Handlers;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Chat.Tests.Handlers;

/// <summary>
///     The message that posts to chat: it finds the channel its target names and posts as the sender. Expected values
///     come from AGENTS.md (a refusal names the missing privileges, a private message goes to an online user).
/// </summary>
public sealed class PostChatMessageHandlerTests
{
	private const string ChannelName = "#lobby";

	private static PostChatMessageHandler HandlerFor(TestApplication app)
	{
		return new PostChatMessageHandler(app.Provider.GetRequiredService<ChannelDirectory>());
	}

	private static async Task<Connection> OpenChannelAsync(TestApplication app)
	{
		var admin = await app.CreateUserAsync("ChannelAdmin", "password", UserPrivilegesExtensions.All);
		var actor = await app.LoginAsync(admin);
		var created = await app.Server.ScopeTo(actor).Channels.CreateAsync(
			new GeneralChannelRecord { Name = ChannelName, Value = new GeneralChannelData() });
		Assert.True(created.IsOk);
		return actor;
	}

	private static async Task<ChannelOutcome> PostAsync(TestApplication app, Connection actor, ChatTarget target,
		string text = "hello")
	{
		var result = await HandlerFor(app).Handle(new PostChatMessage(actor, target, text),
			TestContext.Current.CancellationToken);
		Assert.Null(result.Missing);
		return result.Outcome;
	}

	[Fact]
	public async Task Handle_GeneralChannelTheSenderIsIn_PostsTheMessage()
	{
		using var app = new TestApplication();
		var actor = await OpenChannelAsync(app);
		Assert.True(app.Channels.Find(ChannelName)!.ScopeTo(actor).Join().IsOk);

		var outcome = await PostAsync(app, actor, new ChatTarget.General(ChannelName), "hi there");

		Assert.Equal(ChannelOutcome.Ok, outcome);
		Assert.Equal("hi there", Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events[^1]).Message.Content);
	}

	[Fact]
	public async Task Handle_GeneralChannelTheSenderIsNotIn_RefusesWithNotMember()
	{
		using var app = new TestApplication();
		var actor = await OpenChannelAsync(app);

		var outcome = await PostAsync(app, actor, new ChatTarget.General(ChannelName));

		Assert.Equal(ChannelOutcome.NotMember, outcome);
	}

	[Fact]
	public async Task Handle_SenderWithoutThePermissionToPost_IsDeniedWithTheMissingPermission()
	{
		using var app = new TestApplication();
		await OpenChannelAsync(app);
		var user = await app.CreateUserAsync("Muted", "password",
			UserPrivilegesExtensions.Default & ~UserPrivileges.Player);
		var actor = await app.LoginAsync(user);

		var result = await HandlerFor(app).Handle(
			new PostChatMessage(actor, new ChatTarget.General(ChannelName), "hello"),
			TestContext.Current.CancellationToken);

		Assert.Equal(UserPrivileges.Player, result.Missing);
		Assert.False(result.IsOk);
	}

	[Fact]
	public async Task Handle_GeneralChannelThatDoesNotExist_AnswersNotFound()
	{
		using var app = new TestApplication();
		var actor = await OpenChannelAsync(app);

		var outcome = await PostAsync(app, actor, new ChatTarget.General("#nowhere"));

		Assert.Equal(ChannelOutcome.NotFound, outcome);
	}

	[Fact]
	public async Task Handle_RoomThatIsNotOpen_AnswersNotFound()
	{
		using var app = new TestApplication();
		var actor = await OpenChannelAsync(app);

		var outcome = await PostAsync(app, actor, new ChatTarget.Room(12345));

		Assert.Equal(ChannelOutcome.NotFound, outcome);
	}

	[Fact]
	public async Task Handle_OwnRoomWhenTheSenderIsInNone_AnswersNotFound()
	{
		using var app = new TestApplication();
		var actor = await RoomFixture.PlayerAsync(app, "Roomless");

		var outcome = await PostAsync(app, actor, new ChatTarget.OwnRoom());

		Assert.Equal(ChannelOutcome.NotFound, outcome);
	}

	[Fact]
	public async Task Handle_OwnRoomForAPlayerInOne_PostsToTheRoomChannel()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True(app.RoomChannels.Of(setup.Room).ScopeTo(setup.Creator).Join().IsOk);

		var outcome = await PostAsync(app, setup.Creator, new ChatTarget.OwnRoom());

		Assert.Equal(ChannelOutcome.Ok, outcome);
	}

	[Fact]
	public async Task Handle_RoomById_PostsToThatRoomsChannel()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True(app.RoomChannels.Of(setup.Room).ScopeTo(setup.Creator).Join().IsOk);

		var outcome = await PostAsync(app, setup.Creator, new ChatTarget.Room(setup.Room.Id));

		Assert.Equal(ChannelOutcome.Ok, outcome);
	}

	[Fact]
	public async Task Handle_OwnSpectatorsWhenNobodyIsWatching_AnswersNotFound()
	{
		using var app = new TestApplication();
		var actor = await RoomFixture.PlayerAsync(app, "Alone");

		var outcome = await PostAsync(app, actor, new ChatTarget.OwnSpectators());

		Assert.Equal(ChannelOutcome.NotFound, outcome);
	}

	[Fact]
	public async Task Handle_PrivateMessageToAnOnlineUser_PostsIntoTheirChannel()
	{
		using var app = new TestApplication();
		var sender = await app.LoginAsync(await app.CreateUserAsync("Sender", "password"));
		var receiver = await app.CreateUserAsync("Receiver", "password");
		await app.LoginAsync(receiver);

		var outcome = await PostAsync(app, sender, new ChatTarget.Private(receiver));

		Assert.Equal(ChannelOutcome.Ok, outcome);
		var posted = Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events[^1]);
		Assert.Equal(receiver.Value.Name, posted.Channel.Name);
	}

	[Fact]
	public async Task Handle_PrivateMessageToAnOfflineUser_AnswersNotFound()
	{
		using var app = new TestApplication();
		var sender = await app.LoginAsync(await app.CreateUserAsync("Sender", "password"));
		var offline = await app.CreateUserAsync("Offline", "password");

		var outcome = await PostAsync(app, sender, new ChatTarget.Private(offline));

		Assert.Equal(ChannelOutcome.NotFound, outcome);
	}

	[Fact]
	public async Task Handle_Notice_IsPostedAsANotice()
	{
		using var app = new TestApplication();
		var actor = await OpenChannelAsync(app);
		Assert.True(app.Channels.Find(ChannelName)!.ScopeTo(actor).Join().IsOk);

		var result = await HandlerFor(app).Handle(
			new PostChatMessage(actor, new ChatTarget.General(ChannelName), "notice me", true),
			TestContext.Current.CancellationToken);

		Assert.True(result.IsOk);
		Assert.True(Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events[^1]).Message.IsNotice);
	}

	[Fact]
	public async Task Handle_SenderIsAStableClient_StillResolvesTheGeneralChannel()
	{
		using var app = new TestApplication();
		await OpenChannelAsync(app);
		var player = await RoomFixture.PlayerAsync(app, "Stable");
		Assert.IsType<StableConnection>(player);
		Assert.True(app.Channels.Find(ChannelName)!.ScopeTo(player).Join().IsOk);

		var outcome = await PostAsync(app, player, new ChatTarget.General(ChannelName));

		Assert.Equal(ChannelOutcome.Ok, outcome);
	}
}