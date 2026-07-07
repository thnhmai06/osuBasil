using Bancho.Application.PacketHandlers.Multiplayer;
using Bancho.Domain.Multiplayer;
using Bancho.Protocol.Packets;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchLoadComplete.</summary>
public class MatchLoadCompleteHandlerTests
{
    [Fact]
    public async Task Handle_NotAllPlayingSlotsLoaded_DoesNotBroadcastAllLoaded()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        match.Slots[0].Status = SlotStatus.Playing;
        match.Slots[1].Status = SlotStatus.Playing;
        host.Dequeue();
        guest.Dequeue();
        var handler = new MatchLoadCompleteHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.True(match.Slots[0].Loaded);
        Assert.Empty(host.Dequeue());
    }

    [Fact]
    public async Task Handle_AllPlayingSlotsLoaded_BroadcastsAllPlayersLoaded()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        match.Slots[0].Status = SlotStatus.Playing;
        match.Slots[1].Status = SlotStatus.Playing;
        match.Slots[1].Loaded = true;
        host.Dequeue();
        guest.Dequeue();
        var handler = new MatchLoadCompleteHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.Contains(ServerPacketWriter.MatchAllPlayersLoaded(), Chunk(host.Dequeue()));
    }
}