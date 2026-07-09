using Basil.Application.Abstractions.Social;
using Basil.Application.Abstractions.Users;
using Basil.Application.PacketHandlers.Channels;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.UseCases.Bot;
using Basil.Application.UseCases.Chat;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>
///     Ported from app/api/domains/cho.py's SendMessage (public), scoped to a single channel — no
///     #spectator/#multiplayer routing yet (Phase 7). ICommandDispatcher is a plain substitute here
///     (defaults to a null reply, i.e. "not a recognized command") — CommandDispatcher/MpCommandService
///     have their own tests. Exercises the real <see cref="ChannelMembershipService" />/
///     <see cref="ChatDispatchService" /> chain (not mocked) so these assertions actually cover the
///     chat core the handler delegates to, not just the handler's own (now thin) body.
/// </summary>
public class SendPublicMessageHandlerTests
{
    private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
    private readonly ICommandDispatcher _commandDispatcher = Substitute.For<ICommandDispatcher>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    private SendPublicMessageHandler MakeHandler()
    {
        var channelMembership = new ChannelMembershipService(_sessionRegistry, _channelRegistry);
        var chatDispatch = new ChatDispatchService(_channelRegistry, _sessionRegistry, channelMembership,
            Substitute.For<IUserRepository>(), Substitute.For<IRelationshipRepository>(),
            Substitute.For<IMailRepository>(), _commandDispatcher);
        return new SendPublicMessageHandler(chatDispatch);
    }

    private static BanchoPacketReader MessageReader(string sender, string text, string recipient, int senderId)
    {
        return new BanchoPacketReader(PacketWriter.WriteString(sender)
            .Concat(PacketWriter.WriteString(text))
            .Concat(PacketWriter.WriteString(recipient))
            .Concat(PacketWriter.WriteInt32(senderId))
            .ToArray());
    }

    [Fact]
    public async Task Handle_Silenced_NoOp()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        sender.SilenceEnd = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60;
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
        _channelRegistry.GetByName("#osu").Returns(channel);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hello", "#osu", 1));

        Assert.Empty(sender.Dequeue());
    }

    [Fact]
    public async Task Handle_UnknownChannel_NoOp()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        _channelRegistry.GetByName("#missing").Returns((ChannelSession?)null);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hello", "#missing", 1));

        Assert.Empty(sender.Dequeue());
    }

    [Fact]
    public async Task Handle_NotAMember_NoOp()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
        _channelRegistry.GetByName("#osu").Returns(channel);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hello", "#osu", 1));

        Assert.Empty(sender.Dequeue());
    }

    [Fact]
    public async Task Handle_NoWritePriv_NoOp()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var channel = new ChannelSession(1, "#staff", "Staff", 0, (int)Privileges.Staff, true);
        channel.Join(sender.Id);
        sender.JoinChannel("#staff");
        _channelRegistry.GetByName("#staff").Returns(channel);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hello", "#staff", 1));

        Assert.Empty(sender.Dequeue());
    }

    [Fact]
    public async Task Handle_PlainMessage_BroadcastsToOtherMembersButNotSender()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var member = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
        channel.Join(sender.Id);
        sender.JoinChannel("#osu");
        channel.Join(member.Id);
        member.JoinChannel("#osu");
        _channelRegistry.GetByName("#osu").Returns(channel);
        _sessionRegistry.All.Returns([sender, member]);
        _sessionRegistry.GetById(member.Id).Returns(member);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hello", "#osu", 1));

        Assert.Equal(ServerPacketWriter.SendMessage("cmyui", "hello", "#osu", 1), member.Dequeue());
        Assert.Empty(sender.Dequeue());
    }

    [Fact]
    public async Task Handle_MessageOverLengthLimit_TruncatesTo2000Chars()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var member = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
        channel.Join(sender.Id);
        sender.JoinChannel("#osu");
        channel.Join(member.Id);
        member.JoinChannel("#osu");
        _channelRegistry.GetByName("#osu").Returns(channel);
        _sessionRegistry.All.Returns([sender, member]);
        _sessionRegistry.GetById(member.Id).Returns(member);
        var longText = new string('a', 2500);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", longText, "#osu", 1));

        var expected = ServerPacketWriter.SendMessage("cmyui", new string('a', 2000), "#osu", 1);
        Assert.Equal(expected, member.Dequeue());
    }

    [Fact]
    public async Task Handle_CommandPrefixedMessage_IsBroadcastAsPlainChat()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var member = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
        channel.Join(sender.Id);
        sender.JoinChannel("#osu");
        channel.Join(member.Id);
        member.JoinChannel("#osu");
        _channelRegistry.GetByName("#osu").Returns(channel);
        _sessionRegistry.All.Returns([sender, member]);
        _sessionRegistry.GetById(member.Id).Returns(member);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "!help", "#osu", 1));

        Assert.Equal(ServerPacketWriter.SendMessage("cmyui", "!help", "#osu", 1), member.Dequeue());
        Assert.Empty(sender.Dequeue());
    }

    [Fact]
    public async Task Handle_DispatcherReturnsReply_BroadcastsReplyFromBotToWholeChannel()
    {
        // id=5, not 1 — 1 collides with BotBootstrapService.BotId, and both sender+bot are now looked
        // up via sessionRegistry.GetById(channel member id) for the reply broadcast.
        var sender = new PlayerSession(5, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var bot = new PlayerSession(BotBootstrapService.BotId, "BanchoBot", "bot-token", Privileges.Unrestricted, 0.0)
            { IsBot = true };
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
        channel.Join(sender.Id);
        sender.JoinChannel("#osu");
        _channelRegistry.GetByName("#osu").Returns(channel);
        _sessionRegistry.All.Returns([sender]);
        _sessionRegistry.GetById(sender.Id).Returns(sender);
        _sessionRegistry.GetById(BotBootstrapService.BotId).Returns(bot);
        _commandDispatcher.DispatchAsync(sender, "!roll", null, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("cmyui rolls 42 point(s)"));

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "!roll", "#osu", 1));

        Assert.Equal(
            ServerPacketWriter.SendMessage("BanchoBot", "cmyui rolls 42 point(s)", "#osu", BotBootstrapService.BotId),
            sender.Dequeue());
    }

    [Fact]
    public async Task Handle_DispatcherReturnsMultilineReply_SendsOnePacketPerLine()
    {
        // id=5, not 1 — see Handle_DispatcherReturnsReply_BroadcastsReplyFromBotToWholeChannel's comment.
        var sender = new PlayerSession(5, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var bot = new PlayerSession(BotBootstrapService.BotId, "BasilBot", "bot-token", Privileges.Unrestricted, 0.0)
            { IsBot = true };
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
        channel.Join(sender.Id);
        sender.JoinChannel("#osu");
        _channelRegistry.GetByName("#osu").Returns(channel);
        _sessionRegistry.All.Returns([sender]);
        _sessionRegistry.GetById(sender.Id).Returns(sender);
        _sessionRegistry.GetById(BotBootstrapService.BotId).Returns(bot);
        _commandDispatcher.DispatchAsync(sender, "!faq rules", null, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("Line one\nLine two"));

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "!faq rules", "#osu", 1));

        var expected = ServerPacketWriter.SendMessage("BasilBot", "Line one", "#osu", BotBootstrapService.BotId)
            .Concat(ServerPacketWriter.SendMessage("BasilBot", "Line two", "#osu", BotBootstrapService.BotId))
            .ToArray();
        Assert.Equal(expected, sender.Dequeue());
    }
}