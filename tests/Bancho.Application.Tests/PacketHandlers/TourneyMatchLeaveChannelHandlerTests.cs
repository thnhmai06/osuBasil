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

/// <summary>Ported from app/api/domains/cho.py's TourneyMatchLeaveChannel.</summary>
public class TourneyMatchLeaveChannelHandlerTests
{
    private static BanchoPacketReader ReaderFor(int matchId) => new(PacketWriter.WriteInt32(matchId));

    [Fact]
    public async Task Handle_NotATourneyClient_NoOp()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var observer = MakePlayer(2, "observer");
        observer.Priv = Privileges.Unrestricted | Privileges.Supporter;
        fixture.RegisterAll(host, observer);
        var match = fixture.CreateMatch(host);
        var handler = new TourneyMatchLeaveChannelHandler(fixture.MatchRegistry, fixture.ChannelRegistry, new ChannelMembershipService(fixture.SessionRegistry));

        await handler.HandleAsync(observer, ReaderFor(match.Id));

        Assert.False(observer.InChannel(match.ChatChannelName));
    }

    [Fact]
    public async Task Handle_TourneyClient_LeavesChannelAndIsRemoved()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var observer = MakePlayer(2, "observer");
        observer.Priv = Privileges.Unrestricted | Privileges.Supporter;
        fixture.RegisterAll(host, observer);
        var match = fixture.CreateMatch(host);
        var membership = new ChannelMembershipService(fixture.SessionRegistry);
        var joinHandler = new TourneyMatchJoinChannelHandler(fixture.MatchRegistry, fixture.ChannelRegistry, membership);
        await joinHandler.HandleAsync(observer, ReaderFor(match.Id));
        var handler = new TourneyMatchLeaveChannelHandler(fixture.MatchRegistry, fixture.ChannelRegistry, membership);

        await handler.HandleAsync(observer, ReaderFor(match.Id));

        Assert.DoesNotContain(observer.Id, match.TourneyClients);
        Assert.False(observer.InChannel(match.ChatChannelName));
    }
}
