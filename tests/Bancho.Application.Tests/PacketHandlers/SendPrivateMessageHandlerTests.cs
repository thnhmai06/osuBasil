using Bancho.Application.Abstractions;
using Bancho.Application.Commands;
using Bancho.Application.Configuration;
using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>
/// Ported from app/api/domains/cho.py's SendMessage (private). A PM to the bot session routes
/// through the same command dispatcher as public messages instead of mail/away-message handling.
/// </summary>
public class SendPrivateMessageHandlerTests
{
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRelationshipRepository _relationships = Substitute.For<IRelationshipRepository>();
    private readonly IMailRepository _mail = Substitute.For<IMailRepository>();
    private readonly ICommandDispatcher _commandDispatcher = Substitute.For<ICommandDispatcher>();
    private readonly IOptions<ServerBehaviorOptions> _serverOptions = Options.Create(new ServerBehaviorOptions
    {
        Domain = "test.local", CommandPrefix = "!", MenuIconUrl = "https://x", MenuOnclickUrl = "https://x",
    });

    private SendPrivateMessageHandler MakeHandler() =>
        new(_sessionRegistry, _users, _relationships, _mail, _commandDispatcher, _serverOptions);

    private static BanchoPacketReader MessageReader(string sender, string text, string recipient, int senderId) =>
        new(PacketWriter.WriteString(sender)
            .Concat(PacketWriter.WriteString(text))
            .Concat(PacketWriter.WriteString(recipient))
            .Concat(PacketWriter.WriteInt32(senderId))
            .ToArray());

    [Fact]
    public async Task Handle_SenderSilenced_NoOp()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        sender.SilenceEnd = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60;

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "other", 1));

        _ = _mail.DidNotReceiveWithAnyArgs().CreateAsync(default, default, default!);
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
        _ = _mail.DidNotReceiveWithAnyArgs().CreateAsync(default, default, default!);
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
        _ = _mail.DidNotReceiveWithAnyArgs().CreateAsync(default, default, default!);
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
        var target = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0) { AwayMessage = "gone fishing" };
        target.Status.Action = Domain.Action.Afk;
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
            5, "offlineuser", "offlineuser", null, 1, "xx", 0, 0, 0, 0, 0, 0, 0, 0, null, null, null, null));

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "offlineuser", 1));

        _ = _mail.Received(1).CreateAsync(1, 5, "hi");
        Assert.Empty(sender.Dequeue());
    }

    [Fact]
    public async Task Handle_TargetDoesNotExist_NoOp()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetByName("nobody").Returns((PlayerSession?)null);
        _users.FetchByNameAsync("nobody").Returns((User?)null);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hi", "nobody", 1));

        _ = _mail.DidNotReceiveWithAnyArgs().CreateAsync(default, default, default!);
        Assert.Empty(sender.Dequeue());
    }

    [Fact]
    public async Task Handle_PmToBot_CommandPrefix_DispatchesAndRepliesToSender()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var bot = new PlayerSession(2, "BanchoBot", "bot-token", Privileges.Unrestricted, 0.0, isBotClient: true);
        _sessionRegistry.GetByName("BanchoBot").Returns(bot);
        _commandDispatcher.DispatchAsync(sender, "help", null, bot).Returns(new CommandDispatchResult("here's help", Hidden: false));

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "!help", "BanchoBot", 1));

        var expected = ServerPacketWriter.SendMessage("BanchoBot", "here's help", "cmyui", 2);
        Assert.Equal(expected, sender.Dequeue());
        _ = _mail.DidNotReceiveWithAnyArgs().CreateAsync(default, default, default!);
    }

    [Fact]
    public async Task Handle_PmToBot_NotACommand_NoOp()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var bot = new PlayerSession(2, "BanchoBot", "bot-token", Privileges.Unrestricted, 0.0, isBotClient: true);
        _sessionRegistry.GetByName("BanchoBot").Returns(bot);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "just chatting", "BanchoBot", 1));

        _ = _commandDispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default!, default, default);
        _ = _mail.DidNotReceiveWithAnyArgs().CreateAsync(default, default, default!);
        Assert.Empty(sender.Dequeue());
    }
}
