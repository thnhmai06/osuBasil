using Bancho.Application.Abstractions;
using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;
using NSubstitute;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's Logout + Player.logout.</summary>
public class LogoutHandlerTests
{
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private LogoutHandler MakeHandler() => new(_sessionRegistry, _clock);

    [Fact]
    public void Handle_WithinOneSecondOfLogin_Ignored()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, loginTime: 1000.0);
        _clock.UtcNow.Returns(DateTimeOffset.FromUnixTimeSeconds(1000)); // 0s since login
        var reader = new BanchoPacketReader(PacketWriter.WriteInt32(0));

        MakeHandler().Handle(session, reader);

        _sessionRegistry.DidNotReceive().Remove(Arg.Any<PlayerSession>());
        Assert.Equal("token", session.Token);
    }

    [Fact]
    public void Handle_AfterOneSecond_RemovesFromRegistryAndInvalidatesToken()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, loginTime: 1000.0);
        _clock.UtcNow.Returns(DateTimeOffset.FromUnixTimeSeconds(1002));
        var reader = new BanchoPacketReader(PacketWriter.WriteInt32(0));

        MakeHandler().Handle(session, reader);

        _sessionRegistry.Received(1).Remove(session);
    }

    [Fact]
    public void Handle_UnrestrictedPlayer_BroadcastsLogoutPacketToOthers()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, loginTime: 1000.0);
        var other = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.All.Returns([other]);
        _clock.UtcNow.Returns(DateTimeOffset.FromUnixTimeSeconds(1002));
        var reader = new BanchoPacketReader(PacketWriter.WriteInt32(0));

        MakeHandler().Handle(session, reader);

        Assert.Equal(ServerPacketWriter.Logout(1), other.Dequeue());
    }

    [Fact]
    public void Handle_RestrictedPlayer_DoesNotBroadcastLogout()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Verified, loginTime: 1000.0); // restricted (no Unrestricted)
        var other = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.All.Returns([other]);
        _clock.UtcNow.Returns(DateTimeOffset.FromUnixTimeSeconds(1002));
        var reader = new BanchoPacketReader(PacketWriter.WriteInt32(0));

        MakeHandler().Handle(session, reader);

        Assert.Empty(other.Dequeue());
    }
}
