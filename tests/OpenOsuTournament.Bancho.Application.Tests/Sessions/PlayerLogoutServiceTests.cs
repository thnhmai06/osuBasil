using NSubstitute;
using OpenOsuTournament.Bancho.Application.Abstractions;
using OpenOsuTournament.Bancho.Application.Abstractions.Multiplayer;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.Sessions.Channels;
using OpenOsuTournament.Bancho.Application.Sessions.Multiplayer;
using OpenOsuTournament.Bancho.Application.Tests.PacketHandlers;
using OpenOsuTournament.Bancho.Application.UseCases.Multiplayer;
using OpenOsuTournament.Bancho.Application.UseCases.Spectating;
using OpenOsuTournament.Bancho.Domain.Users;
using OpenOsuTournament.Bancho.Protocol.Packets;

namespace OpenOsuTournament.Bancho.Application.Tests.Sessions;

/// <summary>
///     Ported from Player.logout's match/channel-leaving + playerlist-removal + broadcast, extracted
///     out of LogoutHandler so !reconnect can force the same cleanup on a session without going
///     through the LOGOUT packet's 1-second login-grace-period check (which is packet-specific, not
///     part of "logout" semantics).
/// </summary>
public class PlayerLogoutServiceTests
{
    private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();

    private readonly MatchMembershipService _matchMembership = new(
        Substitute.For<IMatchRegistry>(), Substitute.For<IChannelRegistry>(),
        Substitute.For<IPlayerSessionRegistry>(),
        new ChannelMembershipService(Substitute.For<IPlayerSessionRegistry>()),
        Substitute.For<IMatchPersistenceRepository>(), Substitute.For<IClock>());

    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    private readonly SpectatorService _spectatorService = new(Substitute.For<IChannelRegistry>(),
        new ChannelMembershipService(Substitute.For<IPlayerSessionRegistry>()));

    private PlayerLogoutService MakeService()
    {
        return new PlayerLogoutService(_sessionRegistry, _channelRegistry, _spectatorService, _matchMembership);
    }

    [Fact]
    public void Logout_RemovesFromSessionRegistry()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

        MakeService().Logout(player);

        _sessionRegistry.Received(1).Remove(player);
    }

    [Fact]
    public void Logout_LeavesAllJoinedChannels()
    {
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        channel.Join(player.Id);
        player.JoinChannel("#osu");
        _channelRegistry.GetByName("#osu").Returns(channel);

        MakeService().Logout(player);

        Assert.False(channel.Contains(1));
        Assert.False(player.InChannel("#osu"));
    }

    [Fact]
    public void Logout_UnrestrictedPlayer_BroadcastsLogoutPacket()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var other = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.All.Returns([other]);

        MakeService().Logout(player);

        Assert.Equal(ServerPacketWriter.Logout(1), other.Dequeue());
    }

    [Fact]
    public void Logout_RestrictedPlayer_DoesNotBroadcastLogoutPacket()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Verified, 0.0); // restricted
        var other = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.All.Returns([other]);

        MakeService().Logout(player);

        Assert.Empty(other.Dequeue());
    }

    [Fact]
    public void Logout_WhileSpectating_StopsSpectatingAndClearsHostSpectatorList()
    {
        var host = new PlayerSession(2, "host", "host-token", Privileges.Unrestricted, 0.0);
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        host.AddSpectator(player);
        player.Spectating = host;

        MakeService().Logout(player);

        Assert.Null(player.Spectating);
        Assert.DoesNotContain(player, host.Spectators);
    }

    [Fact]
    public void Logout_WhileInAMatch_LeavesTheMatchSoItDoesNotAccumulateAGhostSlot()
    {
        var channelRegistry = new MultiplayerTestSupport.FakeChannelRegistry();
        var matchRegistry = new MultiplayerTestSupport.FakeMatchRegistry();
        var sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var matchMembership = new MatchMembershipService(matchRegistry, channelRegistry, sessionRegistry,
            new ChannelMembershipService(sessionRegistry), new MultiplayerTestSupport.FakeMatchPersistenceRepository(),
            clock);
        var host = new PlayerSession(1, "host", "token", Privileges.Unrestricted, 0.0);
        sessionRegistry.All.Returns([host]);
        sessionRegistry.GetById(1).Returns(host);
        var match = matchMembership.CreateAsync(host, MultiplayerTestSupport.MakeMatchData(host.Id))
            .GetAwaiter().GetResult()!;
        var service = new PlayerLogoutService(sessionRegistry, channelRegistry, _spectatorService, matchMembership);

        service.Logout(host);

        Assert.Null(host.Match);
        Assert.Null(matchRegistry.GetById(match.Id)); // last player left -> match disposed, not a ghost slot
    }
}