using NSubstitute;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.Sessions.Channels;
using OpenOsuTournament.Bancho.Domain.Users;
using OpenOsuTournament.Bancho.Protocol.Packets;

namespace OpenOsuTournament.Bancho.Application.Tests.Sessions;

/// <summary>
///     Ported from Player.join_channel/leave_channel, shared between client-initiated packets and server-initiated
///     instance membership (spectator, mp).
/// </summary>
public class ChannelMembershipServiceTests
{
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    private ChannelMembershipService MakeService()
    {
        return new ChannelMembershipService(_sessionRegistry);
    }

    private static PlayerSession MakePlayer(int id, string name)
    {
        return new PlayerSession(id, name, "token", Privileges.Unrestricted, 0.0);
    }

    [Fact]
    public void Join_OrdinaryChannel_BroadcastsToEverySessionThatCanRead()
    {
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
        var player = MakePlayer(1, "alice");
        var other = MakePlayer(2, "bob");
        _sessionRegistry.All.Returns([player, other]);

        var joined = MakeService().Join(player, channel);

        Assert.True(joined);
        Assert.True(channel.Contains(1));
        Assert.True(player.InChannel("#osu"));
        Assert.Contains(ServerPacketWriter.ChannelInfo("#osu", "General", 1), Chunk(other.Dequeue()));
    }

    [Fact]
    public void Join_InstanceChannel_OnlyBroadcastsToChannelMembers()
    {
        var channel = new ChannelSession(0, "#spec_9", "topic", 0, 0, false, "#spectator", true);
        var host = MakePlayer(9, "host");
        var joiner = MakePlayer(1, "alice");
        var bystander = MakePlayer(2, "bob");
        channel.Join(host.Id);
        _sessionRegistry.All.Returns([host, joiner, bystander]);
        _sessionRegistry.GetById(host.Id).Returns(host);
        _sessionRegistry.GetById(joiner.Id).Returns(joiner);

        MakeService().Join(joiner, channel);

        Assert.Contains(ServerPacketWriter.ChannelInfo("#spectator", "topic", 2), Chunk(host.Dequeue()));
        Assert.Empty(bystander.Dequeue());
    }

    [Fact]
    public void Join_AlreadyInChannel_ReturnsFalseAndNoBroadcast()
    {
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
        var player = MakePlayer(1, "alice");
        player.JoinChannel("#osu");
        channel.Join(1);

        var joined = MakeService().Join(player, channel);

        Assert.False(joined);
        Assert.Empty(player.Dequeue());
    }

    [Fact]
    public void Part_SendsKickAndBroadcastsUpdatedCount()
    {
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
        var player = MakePlayer(1, "alice");
        var other = MakePlayer(2, "bob");
        player.JoinChannel("#osu");
        channel.Join(1);
        channel.Join(2);
        _sessionRegistry.All.Returns([player, other]);

        MakeService().Part(player, channel);

        Assert.False(channel.Contains(1));
        Assert.False(player.InChannel("#osu"));
        Assert.Contains(ServerPacketWriter.ChannelKick("#osu"), Chunk(player.Dequeue()));
        Assert.Contains(ServerPacketWriter.ChannelInfo("#osu", "General", 1), Chunk(other.Dequeue()));
    }

    [Fact]
    public void Part_WithoutKick_SkipsKickPacket()
    {
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
        var player = MakePlayer(1, "alice");
        player.JoinChannel("#osu");
        channel.Join(1);
        _sessionRegistry.All.Returns([player]);

        MakeService().Part(player, channel, false);

        var dequeued = player.Dequeue();
        Assert.DoesNotContain(ServerPacketWriter.ChannelKick("#osu"), Chunk(dequeued));
    }

    // Handlers concatenate multiple packets into one Dequeue() call; this splits it back for
    // Contains-style assertions without needing exact-offset byte matching.
    private static List<byte[]> Chunk(byte[] data)
    {
        var chunks = new List<byte[]>();
        var offset = 0;
        while (offset < data.Length)
        {
            var length = BitConverter.ToInt32(data, offset + 3);
            var total = 7 + length;
            chunks.Add(data[offset..(offset + total)]);
            offset += total;
        }

        return chunks;
    }
}