using NSubstitute;
using OpenOsuTournament.Bancho.Application.Abstractions;
using OpenOsuTournament.Bancho.Application.Abstractions.Multiplayer;
using OpenOsuTournament.Bancho.Application.PacketHandlers.Core;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.Sessions.Channels;
using OpenOsuTournament.Bancho.Application.Sessions.Multiplayer;
using OpenOsuTournament.Bancho.Application.UseCases.Multiplayer;
using OpenOsuTournament.Bancho.Application.UseCases.Spectating;
using OpenOsuTournament.Bancho.Domain.Users;
using OpenOsuTournament.Bancho.Protocol.Packets;

namespace OpenOsuTournament.Bancho.Application.Tests.PacketHandlers;

/// <summary>
///     Ported from app/api/domains/cho.py's Logout — the 1-second login-grace-period check. The
///     actual cleanup (match/channel-leaving, registry removal, broadcast) is delegated to
///     PlayerLogoutService and covered by PlayerLogoutServiceTests.
/// </summary>
public class LogoutHandlerTests
{
    private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    private LogoutHandler MakeHandler()
    {
        return new LogoutHandler(new PlayerLogoutService(
            _sessionRegistry, _channelRegistry,
            new SpectatorService(Substitute.For<IChannelRegistry>(),
                new ChannelMembershipService(Substitute.For<IPlayerSessionRegistry>())),
            new MatchMembershipService(Substitute.For<IMatchRegistry>(), Substitute.For<IChannelRegistry>(),
                Substitute.For<IPlayerSessionRegistry>(),
                new ChannelMembershipService(Substitute.For<IPlayerSessionRegistry>()),
                Substitute.For<IMatchPersistenceRepository>(), Substitute.For<IClock>())), _clock);
    }

    [Fact]
    public async Task Handle_WithinOneSecondOfLogin_Ignored()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 1000.0);
        _clock.UtcNow.Returns(DateTimeOffset.FromUnixTimeSeconds(1000)); // 0s since login
        var reader = new BanchoPacketReader(PacketWriter.WriteInt32(0));

        await MakeHandler().HandleAsync(session, reader);

        _sessionRegistry.DidNotReceive().Remove(Arg.Any<PlayerSession>());
        Assert.Equal("token", session.Token);
    }

    [Fact]
    public async Task Handle_AfterOneSecond_DelegatesToLogoutService()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 1000.0);
        _clock.UtcNow.Returns(DateTimeOffset.FromUnixTimeSeconds(1002));
        var reader = new BanchoPacketReader(PacketWriter.WriteInt32(0));

        await MakeHandler().HandleAsync(session, reader);

        _sessionRegistry.Received(1).Remove(session);
    }
}