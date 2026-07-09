using Basil.Application.Abstractions.Channels;
using Basil.Application.PacketHandlers.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.UseCases.Spectating;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's StartSpectating.</summary>
public class StartSpectatingHandlerTests
{
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    private static PlayerSession MakePlayer(int id, string name)
    {
        return new PlayerSession(id, name, "token", Privileges.Unrestricted, 0.0);
    }

    private static BanchoPacketReader TargetIdReader(int targetId)
    {
        return new BanchoPacketReader(PacketWriter.WriteInt32(targetId));
    }

    [Fact]
    public async Task Handle_UnknownTarget_NoOp()
    {
        _sessionRegistry.GetById(999).Returns((PlayerSession?)null);
        var handler = new StartSpectatingHandler(_sessionRegistry,
            new SpectatorService(new FakeChannelRegistry(), new ChannelMembershipService(_sessionRegistry, new FakeChannelRegistry())));
        var player = MakePlayer(1, "alice");

        await handler.HandleAsync(player, TargetIdReader(999));

        Assert.Null(player.Spectating);
    }

    [Fact]
    public async Task Handle_NewHost_StartsSpectating()
    {
        var host = MakePlayer(2, "host");
        var player = MakePlayer(1, "alice");
        _sessionRegistry.GetById(2).Returns(host);
        _sessionRegistry.All.Returns([host, player]);
        _sessionRegistry.GetById(1).Returns(player);
        var handler = new StartSpectatingHandler(_sessionRegistry,
            new SpectatorService(new FakeChannelRegistry(), new ChannelMembershipService(_sessionRegistry, new FakeChannelRegistry())));

        await handler.HandleAsync(player, TargetIdReader(2));

        Assert.Same(host, player.Spectating);
        Assert.Contains(player, host.Spectators);
    }

    [Fact]
    public async Task Handle_SameHostAgain_ResendsSpectatorJoinedWithoutRejoiningChannel()
    {
        var host = MakePlayer(2, "host");
        var player = MakePlayer(1, "alice");
        _sessionRegistry.GetById(2).Returns(host);
        _sessionRegistry.GetById(1).Returns(player);
        _sessionRegistry.All.Returns([host, player]);
        var spectatorService =
            new SpectatorService(new FakeChannelRegistry(), new ChannelMembershipService(_sessionRegistry, new FakeChannelRegistry()));
        var handler = new StartSpectatingHandler(_sessionRegistry, spectatorService);
        await handler.HandleAsync(player, TargetIdReader(2));
        host.Dequeue();

        await handler.HandleAsync(player, TargetIdReader(2));

        Assert.Contains(ServerPacketWriter.SpectatorJoined(player.Id), Chunk(host.Dequeue()));
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