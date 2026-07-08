using Basil.Application.PacketHandlers.Multiplayer;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;
using static Basil.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchChangeTeam.</summary>
public class MatchChangeTeamHandlerTests
{
    [Fact]
    public async Task Handle_TogglesBetweenRedAndBlue()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var handler = new MatchChangeTeamHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));
        Assert.Equal(MatchTeams.Blue, match.GetSlot(host.Id)!.Team);

        await handler.HandleAsync(host, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));
        Assert.Equal(MatchTeams.Red, match.GetSlot(host.Id)!.Team);
    }
}