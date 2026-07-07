using Bancho.Application.PacketHandlers;
using Bancho.Protocol;
using Bancho.Application.PacketHandlers.Multiplayer;
using Bancho.Protocol.Packets;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchFailed.</summary>
public class MatchFailedHandlerTests
{
    [Fact]
    public async Task Handle_BroadcastsPlayerFailedWithCorrectSlotId()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        host.Dequeue();
        var handler = new MatchFailedHandler(fixture.MatchMembership);

        await handler.HandleAsync(guest, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.Contains(ServerPacketWriter.MatchPlayerFailed(1), Chunk(host.Dequeue()));
    }
}
