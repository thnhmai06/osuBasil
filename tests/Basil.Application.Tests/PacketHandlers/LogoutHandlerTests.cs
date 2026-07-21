using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>
///     Ported from app/api/domains/cho.py's Logout — the 1-second login-grace-period check. The
///     actual cleanup (match/channel-leaving, registry removal, broadcast) is delegated to
///     PlayerLogoutService and covered by PlayerLogoutServiceTests.
/// </summary>
public class LogoutHandlerTests
{
    private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    private LogoutHandler MakeHandler()
    {
        return new LogoutHandler(new PlayerLogoutService(
            _sessionRegistry, _channelRegistry,
            new SpectatorService(Substitute.For<IChannelRegistry>(),
                new ChannelMembershipService(Substitute.For<IPlayerSessionRegistry>(), Substitute.For<IChannelRegistry>())),
            new MatchMembershipService(Substitute.For<IMatchRegistry>(), Substitute.For<IChannelRegistry>(),
                Substitute.For<IPlayerSessionRegistry>(),
                new ChannelMembershipService(Substitute.For<IPlayerSessionRegistry>(), Substitute.For<IChannelRegistry>()),
                Substitute.For<IMatchPersistenceRepository>(), Substitute.For<IMatchEventBus>(),
                Substitute.For<IMapRepository>())));
    }

    [Fact]
    public async Task Handle_WithinOneSecondOfLogin_Ignored()
    {
        var loginTime = DateTimeOffset.UtcNow;
        var session = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, loginTime);
        var reader = new BanchoPacketReader(PacketWriter.WriteInt32(0));

        await MakeHandler().HandleAsync(session, reader);

        _sessionRegistry.DidNotReceive().Remove(Arg.Any<PlayerSession>());
        Assert.Equal("token", session.Token);
    }

    [Fact]
    public async Task Handle_AfterOneSecond_DelegatesToLogoutService()
    {
        var loginTime = DateTimeOffset.UtcNow.AddSeconds(-2);
        var session = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, loginTime);
        var reader = new BanchoPacketReader(PacketWriter.WriteInt32(0));

        await MakeHandler().HandleAsync(session, reader);

        _sessionRegistry.Received(1).Remove(session);
    }
}
