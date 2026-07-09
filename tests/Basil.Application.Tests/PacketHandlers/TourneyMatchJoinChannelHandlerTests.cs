using Basil.Application.PacketHandlers.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using static Basil.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's TourneyMatchJoinChannel.</summary>
public class TourneyMatchJoinChannelHandlerTests
{
    private static BanchoPacketReader ReaderFor(int matchId)
    {
        return new BanchoPacketReader(PacketWriter.WriteInt32(matchId));
    }

    private static PlayerSession MakeDonator(int id, string name)
    {
        var player = MakePlayer(id, name);
        player.Priv = Privileges.Unrestricted | Privileges.Supporter;
        return player;
    }

    [Fact]
    public async Task Handle_PlayingInTheMatch_DoesNotJoinAsTourneyClient()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var handler = new TourneyMatchJoinChannelHandler(fixture.MatchRegistry, fixture.ChannelRegistry,
            new ChannelMembershipService(fixture.SessionRegistry, fixture.ChannelRegistry));
        host.Priv = Privileges.Unrestricted | Privileges.Supporter;

        await handler.HandleAsync(host, ReaderFor(match.Id));

        Assert.DoesNotContain(host.Id, match.TourneyClients);
    }

    [Fact]
    public async Task Handle_ObserverDonator_JoinsChannelAndBecomesTourneyClient()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var observer = MakeDonator(2, "observer");
        fixture.RegisterAll(host, observer);
        var match = fixture.CreateMatch(host);
        var handler = new TourneyMatchJoinChannelHandler(fixture.MatchRegistry, fixture.ChannelRegistry,
            new ChannelMembershipService(fixture.SessionRegistry, fixture.ChannelRegistry));

        await handler.HandleAsync(observer, ReaderFor(match.Id));

        Assert.Contains(observer.Id, match.TourneyClients);
        Assert.True(observer.InChannel(match.ChatChannelName));
    }
}