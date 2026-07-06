using Bancho.Application.PacketHandlers;
using Bancho.Domain;
using Bancho.Protocol;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.PacketHandlers;

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
