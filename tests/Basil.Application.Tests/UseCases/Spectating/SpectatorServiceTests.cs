using Basil.Application.Abstractions.Channels;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;

namespace Basil.Application.Tests.UseCases.Spectating;

/// <summary>Ported from Player.add_spectator/remove_spectator.</summary>
public class SpectatorServiceTests
{
    private readonly FakeChannelRegistry _channelRegistry = new();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    private SpectatorService MakeService()
    {
        return new SpectatorService(_channelRegistry, new ChannelMembershipService(_sessionRegistry, _channelRegistry));
    }

    private static PlayerSession MakePlayer(int id, string name)
    {
        return new PlayerSession(id, name, "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
    }

    private void RegisterAll(params PlayerSession[] sessions)
    {
        _sessionRegistry.All.Returns(sessions);
        foreach (var session in sessions) _sessionRegistry.GetById(session.Id).Returns(session);
    }

    [Fact]
    public void AddSpectator_FirstSpectator_CreatesChannelAndNotifiesHost()
    {
        var host = MakePlayer(1, "host");
        var spectator = MakePlayer(2, "alice");
        RegisterAll(host, spectator);

        MakeService().AddSpectator(host, spectator);

        Assert.NotNull(_channelRegistry.GetByName("#spec_1"));
        Assert.Same(host, spectator.Spectating);
        Assert.Contains(spectator, host.Spectators);
        Assert.Contains(ServerPacketWriter.SpectatorJoined(spectator.Id), Chunk(host.Dequeue()));
    }

    [Fact]
    public void AddSpectator_SecondSpectator_NotifiesExistingAndNewSpectatorsOfEachOther()
    {
        var host = MakePlayer(1, "host");
        var first = MakePlayer(2, "alice");
        var second = MakePlayer(3, "bob");
        RegisterAll(host, first, second);
        MakeService().AddSpectator(host, first);
        host.Dequeue();
        first.Dequeue();

        MakeService().AddSpectator(host, second);

        Assert.Contains(ServerPacketWriter.FellowSpectatorJoined(second.Id), Chunk(first.Dequeue()));
        Assert.Contains(ServerPacketWriter.FellowSpectatorJoined(first.Id), Chunk(second.Dequeue()));
    }

    [Fact]
    public void AddSpectator_Stealth_DoesNotNotifyHostOrExistingSpectators()
    {
        var host = MakePlayer(1, "host");
        var first = MakePlayer(2, "alice");
        var stealthSpectator = MakePlayer(3, "admin");
        stealthSpectator.Stealth = true;
        RegisterAll(host, first, stealthSpectator);
        MakeService().AddSpectator(host, first);
        host.Dequeue();
        first.Dequeue();

        MakeService().AddSpectator(host, stealthSpectator);

        Assert.DoesNotContain(ServerPacketWriter.SpectatorJoined(stealthSpectator.Id), Chunk(host.Dequeue()));
        // `first` still gets the generic channel_info playercount update (channel membership
        // mechanics don't know about stealth) but never learns a fellow spectator joined.
        Assert.DoesNotContain(ServerPacketWriter.FellowSpectatorJoined(stealthSpectator.Id), Chunk(first.Dequeue()));
        Assert.Contains(ServerPacketWriter.FellowSpectatorJoined(first.Id), Chunk(stealthSpectator.Dequeue()));
    }

    [Fact]
    public void RemoveSpectator_LastSpectator_DeletesChannelAndHostLeaves()
    {
        var host = MakePlayer(1, "host");
        var spectator = MakePlayer(2, "alice");
        RegisterAll(host, spectator);
        MakeService().AddSpectator(host, spectator);

        MakeService().RemoveSpectator(host, spectator);

        Assert.Null(_channelRegistry.GetByName("#spec_1"));
        Assert.Null(spectator.Spectating);
        Assert.DoesNotContain(spectator, host.Spectators);
        Assert.False(host.InChannel("#spec_1"));
    }

    [Fact]
    public void RemoveSpectator_OthersRemain_ChannelSurvivesAndRemainingAreNotified()
    {
        var host = MakePlayer(1, "host");
        var first = MakePlayer(2, "alice");
        var second = MakePlayer(3, "bob");
        RegisterAll(host, first, second);
        var service = MakeService();
        service.AddSpectator(host, first);
        service.AddSpectator(host, second);
        first.Dequeue();

        service.RemoveSpectator(host, second);

        Assert.NotNull(_channelRegistry.GetByName("#spec_1"));
        Assert.Contains(ServerPacketWriter.FellowSpectatorLeft(second.Id), Chunk(first.Dequeue()));
    }

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

    private sealed class FakeChannelRegistry : IChannelRegistry
    {
        private readonly Dictionary<string, ChannelSession> _byName = new();

        public void Seed(IReadOnlyList<Channel> channels)
        {
            throw new NotSupportedException();
        }

        public void Add(ChannelSession channel)
        {
            _byName[channel.Name] = channel;
        }

        public void Remove(string name)
        {
            _byName.Remove(name);
        }

        public ChannelSession? GetByName(string name)
        {
            return _byName.GetValueOrDefault(name);
        }

        public IReadOnlyList<ChannelSession> AutoJoinChannels => throw new NotSupportedException();

        public IReadOnlyList<ChannelSession> All => _byName.Values.ToList();
    }
}