using Basil.Application.Abstractions.Channels;
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
using Action = Basil.Domain.Action;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>
///     Ported from app/api/domains/cho.py's SendMessage (private). Exercises the real
///     <see cref="ChannelMembershipService" />/<see cref="ChatDispatchService" /> chain (not mocked) so
///     these assertions actually cover the chat core the handler delegates to.
/// </summary>
public class SendPrivateMessageHandlerTests
{
    private readonly ICommandDispatcher _commandDispatcher = Substitute.For<ICommandDispatcher>();
    private readonly IMailRepository _mail = Substitute.For<IMailRepository>();
    private readonly IRelationshipRepository _relationships = Substitute.For<IRelationshipRepository>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    private SendPrivateMessageHandler MakeHandler()
    {
        var channelRegistry = Substitute.For<IChannelRegistry>();
        var channelMembership = new ChannelMembershipService(_sessionRegistry, channelRegistry);
        var chatDispatch = new ChatDispatchService(channelRegistry, _sessionRegistry, channelMembership, _users,
            _relationships, _mail, _commandDispatcher);
        return new SendPrivateMessageHandler(chatDispatch);
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
    public async Task Handle_SenderSilenced_NoOp()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        sender.SilenceEnd = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60;

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "other", 1));

        _ = _mail.DidNotReceiveWithAnyArgs().CreateAsync(0, 0, null!);
    }

    [Fact]
    public async Task Handle_TargetBlocksSender_SendsDmBlockedNotice()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var target = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetByName("other").Returns(target);
        _relationships.FetchOneAsync(2, 1).Returns(new Relationship(2, 1, RelationshipType.Block));

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "other", 1));

        Assert.Equal(ServerPacketWriter.UserDmBlocked("other"), sender.Dequeue());
        _ = _mail.DidNotReceiveWithAnyArgs().CreateAsync(0, 0, null!);
    }

    [Fact]
    public async Task Handle_TargetPmPrivateAndNotFriend_SendsDmBlockedNotice()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var target = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0) { PmPrivate = true };
        _sessionRegistry.GetByName("other").Returns(target);
        _relationships.FetchOneAsync(2, 1).Returns((Relationship?)null);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "other", 1));

        Assert.Equal(ServerPacketWriter.UserDmBlocked("other"), sender.Dequeue());
    }

    [Fact]
    public async Task Handle_TargetSilenced_SendsTargetSilencedNotice()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var target = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        target.SilenceEnd = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60;
        _sessionRegistry.GetByName("other").Returns(target);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "other", 1));

        Assert.Equal(ServerPacketWriter.TargetSilenced("other"), sender.Dequeue());
        _ = _mail.DidNotReceiveWithAnyArgs().CreateAsync(0, 0, null!);
    }

    [Fact]
    public async Task Handle_TargetOnline_DeliversLiveAndAlwaysInsertsMail()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var target = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetByName("other").Returns(target);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi there", "other", 1));

        Assert.Equal(ServerPacketWriter.SendMessage("cmyui", "hi there", "other", 1), target.Dequeue());
        _ = _mail.Received(1).CreateAsync(1, 2, "hi there");
    }

    [Fact]
    public async Task Handle_TargetAway_SendsAwayMessageAutoReply()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var target = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0)
            { AwayMessage = "gone fishing" };
        target.Status.Action = Action.Afk;
        _sessionRegistry.GetByName("other").Returns(target);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "other", 1));

        var expected = ServerPacketWriter.SendMessage("other", "gone fishing", "cmyui", 2);
        Assert.Equal(expected, sender.Dequeue());
    }

    [Fact]
    public async Task Handle_TargetOffline_ExistsInDb_InsertsMailOnly()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetByName("offlineuser").Returns((PlayerSession?)null);
        _users.FetchByNameAsync("offlineuser").Returns(new User(
            5, "offlineuser", "offlineuser", null, 1, "xx", 0, 0, 0, 0, 0, 0, 0, 0, null, null, null));

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "offlineuser", 1));

        _ = _mail.Received(1).CreateAsync(1, 5, "hi");
        Assert.Empty(sender.Dequeue());
    }

    [Fact]
    public async Task Handle_TargetIsBot_DispatchesWithNullMatchScopeAndRepliesDirectly_SkipsMail()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var bot = new PlayerSession(BotBootstrapService.BotId, "BanchoBot", "bot-token", Privileges.Unrestricted, 0.0)
            { IsBot = true };
        _sessionRegistry.GetByName("BanchoBot").Returns(bot);
        _commandDispatcher.DispatchAsync(sender, "!roll", null, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("cmyui rolls 7 point(s)"));

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "!roll", "BanchoBot", 1));

        Assert.Equal(
            ServerPacketWriter.SendMessage("BanchoBot", "cmyui rolls 7 point(s)", "BanchoBot",
                BotBootstrapService.BotId),
            sender.Dequeue());
        _ = _mail.DidNotReceiveWithAnyArgs().CreateAsync(0, 0, null!);
    }

    [Fact]
    public async Task Handle_TargetIsBot_DispatcherReturnsNull_NoReplySentNoMail()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var bot = new PlayerSession(BotBootstrapService.BotId, "BanchoBot", "bot-token", Privileges.Unrestricted, 0.0)
            { IsBot = true };
        _sessionRegistry.GetByName("BanchoBot").Returns(bot);
        _commandDispatcher.DispatchAsync(sender, "hi", null, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "BanchoBot", 1));

        Assert.Empty(sender.Dequeue());
        _ = _mail.DidNotReceiveWithAnyArgs().CreateAsync(0, 0, null!);
    }

    [Fact]
    public async Task Handle_TargetIsBot_MultilineReply_SendsOnePacketPerLine()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var bot = new PlayerSession(BotBootstrapService.BotId, "BasilBot", "bot-token", Privileges.Unrestricted, 0.0)
            { IsBot = true };
        _sessionRegistry.GetByName("BasilBot").Returns(bot);
        _commandDispatcher.DispatchAsync(sender, "!faq rules", null, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("Line one\nLine two"));

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "!faq rules", "BasilBot", 1));

        var expected = ServerPacketWriter.SendMessage("BasilBot", "Line one", "BasilBot", BotBootstrapService.BotId)
            .Concat(ServerPacketWriter.SendMessage("BasilBot", "Line two", "BasilBot", BotBootstrapService.BotId))
            .ToArray();
        Assert.Equal(expected, sender.Dequeue());
    }

    [Fact]
    public async Task Handle_TargetDoesNotExist_NoOp()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetByName("nobody").Returns((PlayerSession?)null);
        _users.FetchByNameAsync("nobody").Returns((User?)null);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "nobody", 1));

        _ = _mail.DidNotReceiveWithAnyArgs().CreateAsync(0, 0, null!);
        Assert.Empty(sender.Dequeue());
    }
}