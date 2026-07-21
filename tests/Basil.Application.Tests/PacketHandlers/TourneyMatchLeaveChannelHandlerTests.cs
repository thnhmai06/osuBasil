using Basil.Application.PacketHandlers.Multiplayer;
using Basil.Application.Sessions.Channels;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using static Basil.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's TourneyMatchLeaveChannel.</summary>
public class TourneyMatchLeaveChannelHandlerTests
{
    private static BanchoPacketReader ReaderFor(int matchId)
    {
        return new BanchoPacketReader(PacketWriter.WriteInt32(matchId));
    }

    [Fact]
    public async Task Handle_NotATourneyClient_NoOp()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var observer = MakePlayer(2, "observer");
        observer.Priv = UserPrivileges.Unrestricted | UserPrivileges.Supporter;
        fixture.RegisterAll(host, observer);
        var match = fixture.CreateMatch(host);
        var handler = new TourneyMatchLeaveChannelHandler(fixture.MatchRegistry, fixture.ChannelRegistry,
            new ChannelMembershipService(fixture.SessionRegistry, fixture.ChannelRegistry));

        await handler.HandleAsync(observer, ReaderFor(match.Id));

        Assert.False(observer.InChannel(match.ChatChannelName));
    }

    [Fact]
    public async Task Handle_TourneyClient_LeavesChannelAndIsRemoved()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var observer = MakePlayer(2, "observer");
        observer.Priv = UserPrivileges.Unrestricted | UserPrivileges.Supporter;
        fixture.RegisterAll(host, observer);
        var match = fixture.CreateMatch(host);
        var membership = new ChannelMembershipService(fixture.SessionRegistry, fixture.ChannelRegistry);
        var joinHandler =
            new TourneyMatchJoinChannelHandler(fixture.MatchRegistry, fixture.ChannelRegistry, membership);
        await joinHandler.HandleAsync(observer, ReaderFor(match.Id));
        var handler = new TourneyMatchLeaveChannelHandler(fixture.MatchRegistry, fixture.ChannelRegistry, membership);

        await handler.HandleAsync(observer, ReaderFor(match.Id));

        Assert.DoesNotContain(observer.Id, match.TourneyClients);
        Assert.False(observer.InChannel(match.ChatChannelName));
    }
}