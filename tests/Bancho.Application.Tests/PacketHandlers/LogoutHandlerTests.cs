using Bancho.Application.Abstractions;
using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Spectating;
using Bancho.Domain;
using Bancho.Protocol;
using NSubstitute;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>
/// Ported from app/api/domains/cho.py's Logout — the 1-second login-grace-period check. The
/// actual cleanup (channel-leaving, registry removal, broadcast) is delegated to
/// PlayerLogoutService and covered by PlayerLogoutServiceTests.
/// </summary>
public class LogoutHandlerTests
{
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private LogoutHandler MakeHandler() => new(new PlayerLogoutService(
        _sessionRegistry, _channelRegistry, new SpectatorService(Substitute.For<IChannelRegistry>(), new ChannelMembershipService(Substitute.For<IPlayerSessionRegistry>()))), _clock);

    [Fact]
    public async Task Handle_WithinOneSecondOfLogin_Ignored()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, loginTime: 1000.0);
        _clock.UtcNow.Returns(DateTimeOffset.FromUnixTimeSeconds(1000)); // 0s since login
        var reader = new BanchoPacketReader(PacketWriter.WriteInt32(0));

        await MakeHandler().HandleAsync(session, reader);

        _sessionRegistry.DidNotReceive().Remove(Arg.Any<PlayerSession>());
        Assert.Equal("token", session.Token);
    }

    [Fact]
    public async Task Handle_AfterOneSecond_DelegatesToLogoutService()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, loginTime: 1000.0);
        _clock.UtcNow.Returns(DateTimeOffset.FromUnixTimeSeconds(1002));
        var reader = new BanchoPacketReader(PacketWriter.WriteInt32(0));

        await MakeHandler().HandleAsync(session, reader);

        _sessionRegistry.Received(1).Remove(session);
    }
}
