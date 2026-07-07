using Bancho.Application.Abstractions;
using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Spectating;
using Bancho.Domain;
using Bancho.Protocol;
using NSubstitute;
using Bancho.Application.Abstractions.Channels;
using Bancho.Application.PacketHandlers.Spectating;
using Bancho.Application.Sessions.Channels;
using Bancho.Domain.Users;
using Bancho.Protocol.Packets;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's StopSpectating.</summary>
public class StopSpectatingHandlerTests
{
    private sealed class FakeChannelRegistry : IChannelRegistry
    {
        private readonly Dictionary<string, ChannelSession> _byName = new();
        public void Seed(IReadOnlyList<Channel> channels) => throw new NotSupportedException();
        public void Add(ChannelSession channel) => _byName[channel.Name] = channel;
        public void Remove(string name) => _byName.Remove(name);
        public ChannelSession? GetByName(string name) => _byName.GetValueOrDefault(name);
        public IReadOnlyList<ChannelSession> AutoJoinChannels => throw new NotSupportedException();
        public IReadOnlyList<ChannelSession> All => _byName.Values.ToList();
    }

    private static PlayerSession MakePlayer(int id, string name) => new(id, name, "token", Privileges.Unrestricted, 0.0);

    [Fact]
    public async Task Handle_NotSpectating_NoOp()
    {
        var sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
        var handler = new StopSpectatingHandler(new SpectatorService(new FakeChannelRegistry(), new ChannelMembershipService(sessionRegistry)));
        var player = MakePlayer(1, "alice");

        await handler.HandleAsync(player, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.Null(player.Spectating);
    }

    [Fact]
    public async Task Handle_Spectating_StopsAndClearsHostSpectatorList()
    {
        var sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
        var host = MakePlayer(2, "host");
        var player = MakePlayer(1, "alice");
        sessionRegistry.All.Returns([host, player]);
        sessionRegistry.GetById(2).Returns(host);
        sessionRegistry.GetById(1).Returns(player);
        var spectatorService = new SpectatorService(new FakeChannelRegistry(), new ChannelMembershipService(sessionRegistry));
        spectatorService.AddSpectator(host, player);
        var handler = new StopSpectatingHandler(spectatorService);

        await handler.HandleAsync(player, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.Null(player.Spectating);
        Assert.DoesNotContain(player, host.Spectators);
    }
}
