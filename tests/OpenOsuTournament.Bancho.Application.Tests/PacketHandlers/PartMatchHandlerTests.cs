using OpenOsuTournament.Bancho.Application.PacketHandlers.Multiplayer;
using OpenOsuTournament.Bancho.Protocol.Packets;
using static OpenOsuTournament.Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace OpenOsuTournament.Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchPart.</summary>
public class PartMatchHandlerTests
{
    [Fact]
    public async Task Handle_NotInAMatch_NoOp()
    {
        var fixture = new Fixture();
        var player = MakePlayer(1, "alice");
        var handler = new PartMatchHandler(fixture.MatchMembership);

        await handler.HandleAsync(player, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.Null(player.Match);
    }

    [Fact]
    public async Task Handle_InAMatch_LeavesAndClearsPlayerMatch()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var handler = new PartMatchHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.Null(host.Match);
        Assert.Null(fixture.MatchRegistry.GetById(match.Id));
    }
}