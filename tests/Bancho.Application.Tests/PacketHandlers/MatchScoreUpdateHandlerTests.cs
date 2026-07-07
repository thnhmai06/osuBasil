using Bancho.Application.PacketHandlers.Multiplayer;
using Bancho.Protocol.Packets;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchScoreUpdate.</summary>
public class MatchScoreUpdateHandlerTests
{
    [Fact]
    public async Task Handle_ForwardsRawFrameWithSlotIdInjectedAtByteEleven()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        host.Dequeue();
        var handler = new MatchScoreUpdateHandler(fixture.MatchMembership);
        var frame = new byte[] { 1, 2, 3, 4, 5, 6 };

        await handler.HandleAsync(guest, new BanchoPacketReader(frame));

        var forwarded = Chunk(host.Dequeue()).Single();
        Assert.Equal((int)ServerPackets.MatchScoreUpdate, BitConverter.ToUInt16(forwarded, 0));
        Assert.Equal(1, forwarded[11]); // guest's slot id (1) overwrites byte 11 (frame[4])
        var expectedBody = (byte[])frame.Clone();
        expectedBody[4] = 1;
        Assert.Equal(expectedBody, forwarded[7..]);
    }
}