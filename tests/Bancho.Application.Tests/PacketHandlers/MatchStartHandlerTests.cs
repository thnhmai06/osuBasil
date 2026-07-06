using Bancho.Application.PacketHandlers;
using Bancho.Domain;
using Bancho.Protocol;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchStart, inlining Match.start.</summary>
public class MatchStartHandlerTests
{
    [Fact]
    public async Task Handle_NonHost_NoOp()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        var handler = new MatchStartHandler(fixture.MatchMembership);

        await handler.HandleAsync(guest, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.False(match.InProgress);
    }

    [Fact]
    public async Task Handle_Host_StartsPlayersWithMapAndSkipsPlayersWithoutIt()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        match.Slots[1].Status = SlotStatus.NoMap;
        var handler = new MatchStartHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.True(match.InProgress);
        Assert.Equal(SlotStatus.Playing, match.Slots[0].Status);
        Assert.Equal(SlotStatus.NoMap, match.Slots[1].Status);
    }
}
