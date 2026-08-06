using Basil.Application.Abstractions.Bot;
using Basil.Application.Abstractions.Social;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Packets.Channels;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Chat;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

/// <summary>
///     Ported from app/api/domains/cho.py's SendMessage (public), scoped to a single channel — no
///     #spectator/#multiplayer routing. ICommandDispatcher is a plain substitute here
///     (defaults to a null reply, i.e. "not a recognized command") — CommandDispatcher/MpCommandService
///     have their own tests. Exercises the real <see cref="ChannelMembershipService" />/
///     <see cref="ChatDispatchService" /> chain (not mocked) so these assertions actually cover the
///     chat core the handler delegates to, not just the handler's own (now thin) body.
/// </summary>
public class SendPublicMessageHandlerTests
{
	private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
	private readonly ICommandDispatcher _commandDispatcher = Substitute.For<ICommandDispatcher>();
	private readonly IUserSessionRegistry _sessionRegistry = Substitute.For<IUserSessionRegistry>();

	private SendPublicMessageHandler MakeHandler()
	{
		var channelMembership = new ChannelMembershipService(_sessionRegistry, _channelRegistry, Options.Create(new IrcOptions()));
		var chatDispatch = new ChatDispatchService(_channelRegistry, _sessionRegistry, channelMembership,
			Substitute.For<IUserRepository>(), Substitute.For<IRelationshipRepository>(), _commandDispatcher,
			Substitute.For<IMatchRegistry>(), NullLogger<ChatDispatchService>.Instance);
		return new SendPublicMessageHandler(chatDispatch);
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
	public async Task Handle_Silenced_NoOp()
	{
		var sender = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			SilenceEnd = DateTimeOffset.UtcNow.AddSeconds(60)
		};
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		_channelRegistry.GetByName("#osu").Returns(channel);

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hello", "#osu", 1));

		Assert.Empty(sender.Dequeue());
	}

	[Fact]
	public async Task Handle_UnknownChannel_NoOp()
	{
		var sender = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_channelRegistry.GetByName("#missing").Returns((ChannelSession?)null);

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hello", "#missing", 1));

		Assert.Empty(sender.Dequeue());
	}

	[Fact]
	public async Task Handle_NotAMember_NoOp()
	{
		var sender = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		_channelRegistry.GetByName("#osu").Returns(channel);

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hello", "#osu", 1));

		Assert.Empty(sender.Dequeue());
	}

	[Fact]
	public async Task Handle_NoWritePriv_NoOp()
	{
		var sender = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var channel = new ChannelSession(1, "#staff", "Staff", 0, UserPrivileges.Staff, true);
		channel.Join(sender.Id);
		sender.JoinChannel("#staff");
		_channelRegistry.GetByName("#staff").Returns(channel);

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hello", "#staff", 1));

		Assert.Empty(sender.Dequeue());
	}

	[Fact]
	public async Task Handle_PlainMessage_BroadcastsToOtherMembersButNotSender()
	{
		var sender = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var member = new GameSession(2, "other", "other-token", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch);
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		channel.Join(sender.Id);
		sender.JoinChannel("#osu");
		channel.Join(member.Id);
		member.JoinChannel("#osu");
		_channelRegistry.GetByName("#osu").Returns(channel);
		_sessionRegistry.GameSessions.Returns([sender, member]);
		_sessionRegistry.GetGameByUserId(member.Id).Returns(member);
		_sessionRegistry.GetSessionsByUserId(member.Id).Returns([member]);

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hello", "#osu", 1));

		Assert.Equal(ServerPacketWriter.SendMessage("cmyui", "hello", "#osu", 1), member.Dequeue());
		Assert.Empty(sender.Dequeue());
	}

	[Fact]
	public async Task Handle_MessageOverLengthLimit_TruncatesTo2000Chars()
	{
		var sender = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var member = new GameSession(2, "other", "other-token", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch);
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		channel.Join(sender.Id);
		sender.JoinChannel("#osu");
		channel.Join(member.Id);
		member.JoinChannel("#osu");
		_channelRegistry.GetByName("#osu").Returns(channel);
		_sessionRegistry.GameSessions.Returns([sender, member]);
		_sessionRegistry.GetGameByUserId(member.Id).Returns(member);
		_sessionRegistry.GetSessionsByUserId(member.Id).Returns([member]);
		var longText = new string('a', 2500);

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", longText, "#osu", 1));

		var expected = ServerPacketWriter.SendMessage("cmyui", new string('a', 2000), "#osu", 1);
		Assert.Equal(expected, member.Dequeue());
	}

	[Fact]
	public async Task Handle_CommandPrefixedMessage_IsBroadcastAsPlainChat()
	{
		var sender = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var member = new GameSession(2, "other", "other-token", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch);
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		channel.Join(sender.Id);
		sender.JoinChannel("#osu");
		channel.Join(member.Id);
		member.JoinChannel("#osu");
		_channelRegistry.GetByName("#osu").Returns(channel);
		_sessionRegistry.GameSessions.Returns([sender, member]);
		_sessionRegistry.GetGameByUserId(member.Id).Returns(member);
		_sessionRegistry.GetSessionsByUserId(member.Id).Returns([member]);

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "!help", "#osu", 1));

		Assert.Equal(ServerPacketWriter.SendMessage("cmyui", "!help", "#osu", 1), member.Dequeue());
		Assert.Empty(sender.Dequeue());
	}

	[Fact]
	public async Task Handle_DispatcherReturnsReply_BroadcastsReplyFromBotToWholeChannel()
	{
		// id=5, not 1 — 1 collides with BotBootstrapService.BotId, and both sender+bot are now looked
		// up via sessionRegistry.GetById(channel member id) for the reply broadcast.
		var sender = new GameSession(5, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var bot = new GameSession(BotBootstrapService.BotId, "BanchoBot", "bot-token", UserPrivileges.Unrestricted,
				DateTimeOffset.UnixEpoch)
			{ IsBot = true };
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		channel.Join(sender.Id);
		sender.JoinChannel("#osu");
		_channelRegistry.GetByName("#osu").Returns(channel);
		_sessionRegistry.GameSessions.Returns([sender]);
		_sessionRegistry.GetGameByUserId(sender.Id).Returns(sender);
		_sessionRegistry.GetSessionsByUserId(sender.Id).Returns([sender]);
		_sessionRegistry.GetGameByUserId(BotBootstrapService.BotId).Returns(bot);
		_sessionRegistry.GetSessionsByUserId(BotBootstrapService.BotId).Returns([bot]);
		_commandDispatcher.DispatchAsync(sender, "!roll", null, "#osu", Arg.Any<ICommandReplySink>(), Arg.Any<bool>(),
				Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				call.Arg<ICommandReplySink>()!.Reply("cmyui rolls 42 point(s)");
				return Task.FromResult(true);
			});

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "!roll", "#osu", 1));

		Assert.Equal(
			ServerPacketWriter.SendMessage("BanchoBot", "cmyui rolls 42 point(s)", "#osu", BotBootstrapService.BotId),
			sender.Dequeue());
	}

	[Fact]
	public async Task Handle_DispatcherReturnsMultilineReply_SendsOnePacketPerLine()
	{
		// id=5, not 1 — see Handle_DispatcherReturnsReply_BroadcastsReplyFromBotToWholeChannel's comment.
		var sender = new GameSession(5, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var bot = new GameSession(BotBootstrapService.BotId, "BasilBot", "bot-token", UserPrivileges.Unrestricted,
				DateTimeOffset.UnixEpoch)
			{ IsBot = true };
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		channel.Join(sender.Id);
		sender.JoinChannel("#osu");
		_channelRegistry.GetByName("#osu").Returns(channel);
		_sessionRegistry.GameSessions.Returns([sender]);
		_sessionRegistry.GetGameByUserId(sender.Id).Returns(sender);
		_sessionRegistry.GetSessionsByUserId(sender.Id).Returns([sender]);
		_sessionRegistry.GetGameByUserId(BotBootstrapService.BotId).Returns(bot);
		_sessionRegistry.GetSessionsByUserId(BotBootstrapService.BotId).Returns([bot]);
		_commandDispatcher.DispatchAsync(sender, "!faq rules", null, "#osu", Arg.Any<ICommandReplySink>(),
				Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				call.Arg<ICommandReplySink>()!.Reply("Line one\nLine two");
				return Task.FromResult(true);
			});

		await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "!faq rules", "#osu", 1));

		var expected = ServerPacketWriter.SendMessage("BasilBot", "Line one", "#osu", BotBootstrapService.BotId)
			.Concat(ServerPacketWriter.SendMessage("BasilBot", "Line two", "#osu", BotBootstrapService.BotId))
			.ToArray();
		Assert.Equal(expected, sender.Dequeue());
	}
}