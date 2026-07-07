using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;
using Bancho.Application.PacketHandlers.Multiplayer;
using Bancho.Application.Sessions.Channels;
using Bancho.Domain.Users;
using Bancho.Protocol.Packets;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's TourneyMatchJoinChannel.</summary>
public class TourneyMatchJoinChannelHandlerTests
{
    private static BanchoPacketReader ReaderFor(int matchId) => new(PacketWriter.WriteInt32(matchId));

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
        var handler = new TourneyMatchJoinChannelHandler(fixture.MatchRegistry, fixture.ChannelRegistry, new ChannelMembershipService(fixture.SessionRegistry));
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
        var handler = new TourneyMatchJoinChannelHandler(fixture.MatchRegistry, fixture.ChannelRegistry, new ChannelMembershipService(fixture.SessionRegistry));

        await handler.HandleAsync(observer, ReaderFor(match.Id));

        Assert.Contains(observer.Id, match.TourneyClients);
        Assert.True(observer.InChannel(match.ChatChannelName));
    }
}
