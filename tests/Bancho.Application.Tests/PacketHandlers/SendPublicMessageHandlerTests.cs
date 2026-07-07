using Bancho.Application.PacketHandlers.Channels;
using Bancho.Application.Sessions;
using Bancho.Application.Sessions.Channels;
using Bancho.Domain.Users;
using Bancho.Protocol.Packets;
using NSubstitute;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>
///     Ported from app/api/domains/cho.py's SendMessage (public), scoped to a single channel — no
///     #spectator/#multiplayer routing yet (Phase 7). Bot commands aren't wired up yet, so a
///     command-prefixed message is just broadcast as plain chat.
/// </summary>
public class SendPublicMessageHandlerTests
{
    private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    private SendPublicMessageHandler MakeHandler()
    {
        return new SendPublicMessageHandler(_channelRegistry, _sessionRegistry);
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

        await MakeHandler().HandleAsync(sender, MessageReader("cmyui", "!help", "#osu", 1));

        Assert.Equal(ServerPacketWriter.SendMessage("cmyui", "!help", "#osu", 1), member.Dequeue());
        Assert.Empty(sender.Dequeue());
    }
}