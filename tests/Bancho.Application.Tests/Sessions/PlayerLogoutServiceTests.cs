using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Spectating;
using Bancho.Domain;
using NSubstitute;

namespace Bancho.Application.Tests.Sessions;

/// <summary>
/// Ported from Player.logout's channel-leaving + playerlist-removal + broadcast, extracted out
/// of LogoutHandler so !reconnect can force the same cleanup on a session without going through
/// the LOGOUT packet's 1-second login-grace-period check (which is packet-specific, not part of
/// "logout" semantics).
/// </summary>
public class PlayerLogoutServiceTests
{
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
    private readonly SpectatorService _spectatorService = new(Substitute.For<IChannelRegistry>(), new ChannelMembershipService(Substitute.For<IPlayerSessionRegistry>()));

    private PlayerLogoutService MakeService() => new(_sessionRegistry, _channelRegistry, _spectatorService);

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
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, autoJoin: true);
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

        Assert.Equal(Protocol.ServerPacketWriter.Logout(1), other.Dequeue());
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
}
