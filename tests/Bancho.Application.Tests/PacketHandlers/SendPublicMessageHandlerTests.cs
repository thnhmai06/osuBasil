using Bancho.Application.Commands;
using Bancho.Application.Configuration;
using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's SendMessage (public), scoped to a single channel — no #spectator/#multiplayer routing yet (Phase 7).</summary>
public class SendPublicMessageHandlerTests
{
    private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly ICommandDispatcher _commandDispatcher = Substitute.For<ICommandDispatcher>();
    private readonly IOptions<ServerBehaviorOptions> _serverOptions = Options.Create(new ServerBehaviorOptions
    {
        Domain = "test.local", CommandPrefix = "!", MenuIconUrl = "https://x", MenuOnclickUrl = "https://x",
    });

    private SendPublicMessageHandler MakeHandler() => new(_channelRegistry, _sessionRegistry, _commandDispatcher, _serverOptions);

    private static BanchoPacketReader MessageReader(string sender, string text, string recipient, int senderId) =>
        new(PacketWriter.WriteString(sender)
            .Concat(PacketWriter.WriteString(text))
            .Concat(PacketWriter.WriteString(recipient))
            .Concat(PacketWriter.WriteInt32(senderId))
            .ToArray());

    [Fact]
    public async Task Handle_Silenced_NoOp()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        sender.SilenceEnd = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60;
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, autoJoin: true);
        _channelRegistry.GetByName("#osu").Returns(channel);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hello", "#osu", 1));

        _ = _commandDispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Handle_UnknownChannel_NoOp()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        _channelRegistry.GetByName("#missing").Returns((ChannelSession?)null);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hello", "#missing", 1));

        _ = _commandDispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Handle_NotAMember_NoOp()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, autoJoin: true);
        _channelRegistry.GetByName("#osu").Returns(channel);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hello", "#osu", 1));

        _ = _commandDispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Handle_NoWritePriv_NoOp()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var channel = new ChannelSession(1, "#staff", "Staff", 0, (int)Privileges.Staff, autoJoin: true);
        channel.Join(sender.Id);
        sender.JoinChannel("#staff");
        _channelRegistry.GetByName("#staff").Returns(channel);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hello", "#staff", 1));

        _ = _commandDispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Handle_PlainMessage_BroadcastsToOtherMembersButNotSender()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var member = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, autoJoin: true);
        channel.Join(sender.Id);
        sender.JoinChannel("#osu");
        channel.Join(member.Id);
        member.JoinChannel("#osu");
        _channelRegistry.GetByName("#osu").Returns(channel);
        _sessionRegistry.All.Returns([sender, member]);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "hello", "#osu", 1));

        Assert.Equal(ServerPacketWriter.SendMessage("cmyui", "hello", "#osu", 1), member.Dequeue());
        Assert.Empty(sender.Dequeue());
    }

    [Fact]
    public async Task Handle_MessageOverLengthLimit_TruncatesTo2000Chars()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var member = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, autoJoin: true);
        channel.Join(sender.Id);
        sender.JoinChannel("#osu");
        channel.Join(member.Id);
        member.JoinChannel("#osu");
        _channelRegistry.GetByName("#osu").Returns(channel);
        _sessionRegistry.All.Returns([sender, member]);
        var longText = new string('a', 2500);

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", longText, "#osu", 1));

        var expected = ServerPacketWriter.SendMessage("cmyui", new string('a', 2000), "#osu", 1);
        Assert.Equal(expected, member.Dequeue());
    }

    [Fact]
    public async Task Handle_CommandPrefix_DispatchesAndBroadcastsBotResponse()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var member = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, autoJoin: true);
        channel.Join(sender.Id);
        sender.JoinChannel("#osu");
        channel.Join(member.Id);
        member.JoinChannel("#osu");
        _channelRegistry.GetByName("#osu").Returns(channel);
        _sessionRegistry.All.Returns([sender, member]);
        _commandDispatcher.DispatchAsync(sender, "help", channel, null).Returns(new CommandDispatchResult("here's help", Hidden: false));

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "!help", "#osu", 1));

        var expected = ServerPacketWriter.SendMessage("BanchoBot", "here's help", "#osu", 1);
        Assert.Equal(expected, member.Dequeue());
        Assert.Equal(expected, sender.Dequeue());
    }

    [Fact]
    public async Task Handle_HiddenCommandResponse_OnlySentToStaffMembers()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var staffMember = new PlayerSession(2, "mod", "mod-token", Privileges.Unrestricted | Privileges.Moderator, 0.0);
        var regularMember = new PlayerSession(3, "regular", "regular-token", Privileges.Unrestricted, 0.0);
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, autoJoin: true);
        foreach (var p in new[] { sender, staffMember, regularMember })
        {
            channel.Join(p.Id);
            p.JoinChannel("#osu");
        }
        _channelRegistry.GetByName("#osu").Returns(channel);
        _sessionRegistry.All.Returns([sender, staffMember, regularMember]);
        _commandDispatcher.DispatchAsync(sender, "silence x", channel, null).Returns(new CommandDispatchResult("silenced x", Hidden: true));

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "!silence x", "#osu", 1));

        Assert.NotEmpty(staffMember.Dequeue());
        Assert.Empty(regularMember.Dequeue());
        Assert.Empty(sender.Dequeue());
    }

    [Fact]
    public async Task Handle_CommandWithNullResponse_NoPacketsSent()
    {
        var sender = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, autoJoin: true);
        channel.Join(sender.Id);
        sender.JoinChannel("#osu");
        _channelRegistry.GetByName("#osu").Returns(channel);
        _sessionRegistry.All.Returns([sender]);
        _commandDispatcher.DispatchAsync(sender, "silentcmd", channel, null).Returns(new CommandDispatchResult(null, Hidden: false));

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "!silentcmd", "#osu", 1));

        Assert.Empty(sender.Dequeue());
    }
}
