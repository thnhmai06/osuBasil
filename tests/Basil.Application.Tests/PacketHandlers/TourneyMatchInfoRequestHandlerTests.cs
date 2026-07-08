using Basil.Application.PacketHandlers.Multiplayer;
using Basil.Application.UseCases.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using static Basil.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's TourneyMatchInfoRequest.</summary>
public class TourneyMatchInfoRequestHandlerTests
{
    private static BanchoPacketReader ReaderFor(int matchId)
    {
        return new BanchoPacketReader(PacketWriter.WriteInt32(matchId));
    }

    [Fact]
    public async Task Handle_NonDonator_NoOp()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var requester = MakePlayer(2, "req");
        fixture.RegisterAll(host, requester);
        var match = fixture.CreateMatch(host);
        var handler = new TourneyMatchInfoRequestHandler(fixture.MatchRegistry);

        await handler.HandleAsync(requester, ReaderFor(match.Id));

        Assert.Empty(requester.Dequeue());
    }

    [Fact]
    public async Task Handle_Donator_SendsUpdateMatchWithoutPassword()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var requester = MakePlayer(2, "req");
        requester.Priv = Privileges.Unrestricted | Privileges.Supporter;
        fixture.RegisterAll(host, requester);
        var match = fixture.CreateMatch(host);
        var handler = new TourneyMatchInfoRequestHandler(fixture.MatchRegistry);

        await handler.HandleAsync(requester, ReaderFor(match.Id));

        Assert.Contains(ServerPacketWriter.UpdateMatch(MatchPacketDataMapper.ToPacketData(match), false),
            Chunk(requester.Dequeue()));
    }
}