using Basil.Application.PacketHandlers.Multiplayer;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;
using static Basil.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchHasBeatmap.</summary>
public class MatchHasBeatmapHandlerTests
{
    [Fact]
    public async Task Handle_SetsSlotStatusToNotReady()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.Slots[0].Status = SlotStatus.NoMap;
        var handler = new MatchHasBeatmapHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.Equal(SlotStatus.NotReady, match.GetSlot(host.Id)!.Status);
    }
}