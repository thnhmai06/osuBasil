using Bancho.Application.PacketHandlers.Multiplayer;
using Bancho.Domain.Multiplayer;
using Bancho.Protocol.Packets;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchNoBeatmap.</summary>
public class MatchNoBeatmapHandlerTests
{
    [Fact]
    public async Task Handle_SetsSlotStatusToNoMap()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var handler = new MatchNoBeatmapHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.Equal(SlotStatus.NoMap, match.GetSlot(host.Id)!.Status);
    }
}