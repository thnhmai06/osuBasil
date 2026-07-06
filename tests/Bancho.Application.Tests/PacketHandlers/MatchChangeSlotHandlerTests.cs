using Bancho.Application.PacketHandlers;
using Bancho.Domain;
using Bancho.Protocol;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchChangeSlot.</summary>
public class MatchChangeSlotHandlerTests
{
    private static BanchoPacketReader ReaderFor(int slotId) => new(PacketWriter.WriteInt32(slotId));

    [Fact]
    public async Task Handle_TargetSlotNotOpen_NoOp()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.Slots[2].Status = SlotStatus.NotReady;
        match.Slots[2].PlayerId = 99;
        var handler = new MatchChangeSlotHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, ReaderFor(2));

        Assert.Equal(0, match.GetSlotId(host.Id));
    }

    [Fact]
    public async Task Handle_TargetSlotOpen_MovesPlayerAndResetsOldSlot()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var handler = new MatchChangeSlotHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, ReaderFor(5));

        Assert.Equal(5, match.GetSlotId(host.Id));
        Assert.True(match.Slots[0].Empty);
    }
}
