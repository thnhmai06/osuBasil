using Basil.Application.Abstractions.Bot;
using Basil.Application.Abstractions.Social;
using Basil.Application.Abstractions.Users;
using Basil.Application.Packets.Channels;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Chat;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Login;
using Basil.Domain.Social;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

/// <summary>
///     Ported from app/api/domains/cho.py's SendMessage (private). Exercises the real
///     <see cref="ChannelMembershipService" />/<see cref="ChatDispatchService" /> chain (not mocked) so
///     these assertions actually cover the chat core the handler delegates to.
/// </summary>
public class SendPrivateMessageHandlerTests
{
	private readonly ICommandDispatcher _commandDispatcher = Substitute.For<ICommandDispatcher>();
	private readonly IRelationshipRepository _relationships = Substitute.For<IRelationshipRepository>();
	private readonly IUserSessionRegistry _sessionRegistry = Substitute.For<IUserSessionRegistry>();
	private readonly IUserRepository _users = Substitute.For<IUserRepository>();

	private SendPrivateMessageHandler MakeHandler()
	{
		var channelRegistry = Substitute.For<IChannelRegistry>();
		var channelMembership = new ChannelMembershipService(_sessionRegistry, channelRegistry);
		var matchRegistry = Substitute.For<IMatchRegistry>();
		var chatDispatch = new ChatDispatchService(channelRegistry, _sessionRegistry, channelMembership, _users,
			_relationships, _commandDispatcher, matchRegistry, NullLogger<ChatDispatchService>.Instance);
		return new SendPrivateMessageHandler(chatDispatch);
	}

	private static PacketReader MessageReader(string sender, string text, string recipient, int senderId)
	{
		return new PacketReader(BinaryWriter.WriteString(sender)
			.Concat(BinaryWriter.WriteString(text))
			.Concat(BinaryWriter.WriteString(recipient))
			.Concat(BinaryWriter.WriteInt32(senderId))
			.ToArray());
	}

	[Fact]
	public async Task Handle_SenderSilenced_NoOp()
	{
		var sender = new UserSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			SilenceEnd = DateTimeOffset.UtcNow.AddSeconds(60)
		};

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "other", 1));

		Assert.Empty(sender.Dequeue());
	}

	[Fact]
	public async Task Handle_TargetBlocksSender_SendsDmBlockedNotice()
	{
		var sender = new UserSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var target = new UserSession(2, "other", "other-token", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch);
		_sessionRegistry.GetByName("other").Returns(target);
		_relationships.FetchOneAsync(2, 1).Returns(new Relationship(2, 1, RelationshipType.Block));

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "other", 1));

		Assert.Equal(ServerPacketWriter.UserDmBlocked("other"), sender.Dequeue());
	}

	[Fact]
	public async Task Handle_TargetPmPrivateAndNotFriend_SendsDmBlockedNotice()
	{
		var sender = new UserSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var target = new UserSession(2, "other", "other-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
			{ PmPrivate = true };
		_sessionRegistry.GetByName("other").Returns(target);
		_relationships.FetchOneAsync(2, 1).Returns((Relationship?)null);

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "other", 1));

		Assert.Equal(ServerPacketWriter.UserDmBlocked("other"), sender.Dequeue());
	}

	[Fact]
	public async Task Handle_TargetSilenced_SendsTargetSilencedNotice()
	{
		var sender = new UserSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var target = new UserSession(2, "other", "other-token", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch)
		{
			SilenceEnd = DateTimeOffset.UtcNow.AddSeconds(60)
		};
		_sessionRegistry.GetByName("other").Returns(target);

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "other", 1));

		Assert.Equal(ServerPacketWriter.TargetSilenced("other"), sender.Dequeue());
	}

	[Fact]
	public async Task Handle_TargetOnline_DeliversLive()
	{
		var sender = new UserSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var target = new UserSession(2, "other", "other-token", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch);
		_sessionRegistry.GetByName("other").Returns(target);

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi there", "other", 1));

		Assert.Equal(ServerPacketWriter.SendMessage("cmyui", "hi there", "other", 1), target.Dequeue());
	}

	[Fact]
	public async Task Handle_TargetAway_SendsAwayMessageAutoReply()
	{
		var sender = new UserSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var target = new UserSession(2, "other", "other-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			AwayMessage = "gone fishing",
			Status =
			{
				UserActivity = UserActivity.Afk
			}
		};
		_sessionRegistry.GetByName("other").Returns(target);

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "other", 1));

		var expected = ServerPacketWriter.SendMessage("other", "gone fishing", "cmyui", 2);
		Assert.Equal(expected, sender.Dequeue());
	}

	[Fact]
	public async Task Handle_TargetOffline_ExistsInDb_NoOp()
	{
		var sender = new UserSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_sessionRegistry.GetByName("offlineuser").Returns((UserSession?)null);
		_users.FetchByNameAsync("offlineuser").Returns(new User(
			5, "offlineuser", Country.Xx, UserPrivileges.Unrestricted, default));

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "offlineuser", 1));

		Assert.Empty(sender.Dequeue());
	}

	[Fact]
	public async Task Handle_TargetIsBot_DispatchesWithNullMatchScopeAndRepliesDirectly()
	{
		var sender = new UserSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var bot = new UserSession(BotBootstrapService.BotId, "BanchoBot", "bot-token", UserPrivileges.Unrestricted,
				DateTimeOffset.UnixEpoch)
			{ IsBot = true };
		_sessionRegistry.GetByName("BanchoBot").Returns(bot);
		_commandDispatcher.DispatchAsync(sender, "!roll", null, null, Arg.Any<ICommandReplySink>(), Arg.Any<bool>(),
				Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				call.Arg<ICommandReplySink>()!.Reply("cmyui rolls 7 point(s)");
				return Task.FromResult(true);
			});

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "!roll", "BanchoBot", 1));

		Assert.Equal(
			ServerPacketWriter.SendMessage("BanchoBot", "cmyui rolls 7 point(s)", "cmyui",
				BotBootstrapService.BotId),
			sender.Dequeue());
	}

	[Fact]
	public async Task Handle_TargetIsBot_DispatcherReturnsNull_NoReplySent()
	{
		var sender = new UserSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var bot = new UserSession(BotBootstrapService.BotId, "BanchoBot", "bot-token", UserPrivileges.Unrestricted,
				DateTimeOffset.UnixEpoch)
			{ IsBot = true };
		_sessionRegistry.GetByName("BanchoBot").Returns(bot);
		_commandDispatcher.DispatchAsync(sender, "hi", null, null, Arg.Any<ICommandReplySink>(), Arg.Any<bool>(),
				Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(false));

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "BanchoBot", 1));

		Assert.Empty(sender.Dequeue());
	}

	[Fact]
	public async Task Handle_TargetIsBot_MultilineReply_SendsOnePacketPerLine()
	{
		var sender = new UserSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var bot = new UserSession(BotBootstrapService.BotId, "BasilBot", "bot-token", UserPrivileges.Unrestricted,
				DateTimeOffset.UnixEpoch)
			{ IsBot = true };
		_sessionRegistry.GetByName("BasilBot").Returns(bot);
		_commandDispatcher.DispatchAsync(sender, "!faq rules", null, null, Arg.Any<ICommandReplySink>(),
				Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				call.Arg<ICommandReplySink>()!.Reply("Line one\nLine two");
				return Task.FromResult(true);
			});

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "!faq rules", "BasilBot", 1));

		var expected = ServerPacketWriter.SendMessage("BasilBot", "Line one", "cmyui", BotBootstrapService.BotId)
			.Concat(ServerPacketWriter.SendMessage("BasilBot", "Line two", "cmyui", BotBootstrapService.BotId))
			.ToArray();
		Assert.Equal(expected, sender.Dequeue());
	}

	[Fact]
	public async Task Handle_TargetDoesNotExist_NoOp()
	{
		var sender = new UserSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_sessionRegistry.GetByName("nobody").Returns((UserSession?)null);
		_users.FetchByNameAsync("nobody").Returns((User?)null);

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "nobody", 1));

		Assert.Empty(sender.Dequeue());
	}
}