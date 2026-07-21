using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's UserPresenceRequest (@register(ClientPackets.USER_PRESENCE_REQUEST)).</summary>
public class UserPresenceRequestHandlerTests
{
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    [Fact]
    public async Task Handle_KnownTarget_EnqueuesTheirPresence()
    {
        var self = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        var target = new PlayerSession(2, "target", "target-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        _sessionRegistry.GetById(2).Returns(target);
        var reader = new BanchoPacketReader(PacketWriter.WriteI32List([2]));

        await new UserPresenceRequestHandler(_sessionRegistry).HandleAsync(self, reader);

        // countryCode 244 = CountryCode.Xx (unset default) — the enum has no 0 member.
        Assert.Equal(
            ServerPacketWriter.UserPresence(2, "target", 0, 244, (int)ClientPrivileges.Player, 0, 0.0, 0.0, 0),
            self.Dequeue());
    }

    [Fact]
    public async Task Handle_UnknownTarget_NothingEnqueued()
    {
        var self = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        _sessionRegistry.GetById(2).Returns((PlayerSession?)null);
        var reader = new BanchoPacketReader(PacketWriter.WriteI32List([2]));

        await new UserPresenceRequestHandler(_sessionRegistry).HandleAsync(self, reader);

        Assert.Empty(self.Dequeue());
    }
}