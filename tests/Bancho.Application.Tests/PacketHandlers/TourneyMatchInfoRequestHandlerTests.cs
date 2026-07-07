using Bancho.Application.PacketHandlers;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;
using Bancho.Protocol;
using Bancho.Application.PacketHandlers.Multiplayer;
using Bancho.Domain.Users;
using Bancho.Protocol.Packets;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's TourneyMatchInfoRequest.</summary>
public class TourneyMatchInfoRequestHandlerTests
{
    private static BanchoPacketReader ReaderFor(int matchId) => new(PacketWriter.WriteInt32(matchId));

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

        Assert.Contains(ServerPacketWriter.UpdateMatch(MatchPacketDataMapper.ToPacketData(match), sendPassword: false), Chunk(requester.Dequeue()));
    }
}
