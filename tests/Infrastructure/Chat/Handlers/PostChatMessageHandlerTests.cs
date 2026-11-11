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
using Basil.Application.Servers.Objects;

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
		Assert.True(created == ServerOutcome.Ok);
		return actor;
	}

	private static async Task<ChannelOutcome> PostAsync(TestApplication app, Connection actor, ChatTarget target,
		string text = "hello")
	{
		var result = await HandlerFor(app).Handle(new PostChatMessage(actor, target, text),
			TestContext.Current.CancellationToken);
		Assert.NotEqual(ChannelOutcome.Denied, result);
		return result;
	}

	[Fact]
	public async Task GeneralSenderIn_PostsMessage()
	{
		using var app = new TestApplication();
		var actor = await OpenChannelAsync(app);
		Assert.True(app.Channels.Find(ChannelName).ScopeTo(actor).Join() != ChannelOutcome.Ok);

		var outcome = await PostAsync(app, actor, new ChatTarget.General(ChannelName), "hi there");

		Assert.Equal(ChannelOutcome.Ok, outcome);
		Assert.Equal("hi there", Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events[^1]).Message.Content);
	}

	[Fact]
	public async Task GeneralSenderOut_RefusesNotMember()
	{
		using var app = new TestApplication();
		var actor = await OpenChannelAsync(app);

		var outcome = await PostAsync(app, actor, new ChatTarget.General(ChannelName));

		Assert.Equal(ChannelOutcome.NotMember, outcome);
	}

	[Fact]
	public async Task NoPostPrivilege_DeniedMissing()
	{
		using var app = new TestApplication();
		await OpenChannelAsync(app);
		var user = await app.CreateUserAsync("Muted", "password",
			UserPrivilegesExtensions.Default & ~UserPrivileges.Player);
		var actor = await app.LoginAsync(user);

		var result = await HandlerFor(app).Handle(
			new PostChatMessage(actor, new ChatTarget.General(ChannelName), "hello"),
			TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Denied, result);
		Assert.False(result == ChannelOutcome.Ok);
	}

	[Fact]
	public async Task GeneralMissing_AnswersNotFound()
	{
		using var app = new TestApplication();
		var actor = await OpenChannelAsync(app);

		var outcome = await PostAsync(app, actor, new ChatTarget.General("#nowhere"));

		Assert.Equal(ChannelOutcome.NotFound, outcome);
	}

	[Fact]
	public async Task RoomNotOpen_AnswersNotFound()
	{
		using var app = new TestApplication();
		var actor = await OpenChannelAsync(app);

		var outcome = await PostAsync(app, actor, new ChatTarget.Room(12345));

		Assert.Equal(ChannelOutcome.NotFound, outcome);
	}

	[Fact]
	public async Task OwnRoomNone_AnswersNotFound()
	{
		using var app = new TestApplication();
		var actor = await RoomFixture.PlayerAsync(app, "Roomless");

		var outcome = await PostAsync(app, actor, new ChatTarget.OwnRoom());

		Assert.Equal(ChannelOutcome.NotFound, outcome);
	}

	[Fact]
	public async Task OwnRoomPlayer_PostsRoomChannel()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True(app.RoomChannels.Of(setup.Room).ScopeTo(setup.Creator).Join() == ChannelOutcome.Ok);

		var outcome = await PostAsync(app, setup.Creator, new ChatTarget.OwnRoom());

		Assert.Equal(ChannelOutcome.Ok, outcome);
	}

	[Fact]
	public async Task RoomById_PostsRoomChannel()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True(app.RoomChannels.Of(setup.Room).ScopeTo(setup.Creator).Join() == ChannelOutcome.Ok);

		var outcome = await PostAsync(app, setup.Creator, new ChatTarget.Room(setup.Room.Id));

		Assert.Equal(ChannelOutcome.Ok, outcome);
	}

	[Fact]
	public async Task NoSpectators_AnswersNotFound()
	{
		using var app = new TestApplication();
		var actor = await RoomFixture.PlayerAsync(app, "Alone");

		var outcome = await PostAsync(app, actor, new ChatTarget.OwnSpectators());

		Assert.Equal(ChannelOutcome.NotFound, outcome);
	}

	[Fact]
	public async Task PrivateOnline_PostsIntoChannel()
	{
		using var app = new TestApplication();
		var sender = await app.LoginAsync(await app.CreateUserAsync("Sender", "password"));
		var receiver = await app.CreateUserAsync("Receiver", "password");
		await app.LoginAsync(receiver);

		var outcome = await PostAsync(app, sender, new ChatTarget.Private(receiver.Id));

		Assert.Equal(ChannelOutcome.Ok, outcome);
		var posted = Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events[^1]);
		Assert.Equal(receiver.Value.Name, posted.Channel.Name);
	}

	[Fact]
	public async Task PrivateOffline_AnswersNotFound()
	{
		using var app = new TestApplication();
		var sender = await app.LoginAsync(await app.CreateUserAsync("Sender", "password"));
		var offline = await app.CreateUserAsync("Offline", "password");

		var outcome = await PostAsync(app, sender, new ChatTarget.Private(offline.Id));

		Assert.Equal(ChannelOutcome.NotFound, outcome);
	}

	[Fact]
	public async Task Notice_PostedAsNotice()
	{
		using var app = new TestApplication();
		var actor = await OpenChannelAsync(app);
		Assert.True(app.Channels.Find(ChannelName).ScopeTo(actor).Join() != ChannelOutcome.Ok);

		var result = await HandlerFor(app).Handle(
			new PostChatMessage(actor, new ChatTarget.General(ChannelName), "notice me", true),
			TestContext.Current.CancellationToken);

		Assert.True(result == ChannelOutcome.Ok);
		Assert.True(Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events[^1]).Message.IsNotice);
	}

	[Fact]
	public async Task StableSender_ResolvesGeneral()
	{
		using var app = new TestApplication();
		await OpenChannelAsync(app);
		var player = await RoomFixture.PlayerAsync(app, "Stable");
		Assert.IsType<StableConnection>(player);
		Assert.True(app.Channels.Find(ChannelName).ScopeTo(player).Join() != ChannelOutcome.Ok);

		var outcome = await PostAsync(app, player, new ChatTarget.General(ChannelName));

		Assert.Equal(ChannelOutcome.Ok, outcome);
	}
}