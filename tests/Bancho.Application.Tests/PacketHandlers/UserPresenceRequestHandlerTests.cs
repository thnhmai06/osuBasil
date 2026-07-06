using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;
using NSubstitute;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's UserPresenceRequest (@register(ClientPackets.USER_PRESENCE_REQUEST)).</summary>
public class UserPresenceRequestHandlerTests
{
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    [Fact]
    public async Task Handle_KnownTarget_EnqueuesTheirPresence()
    {
        var self = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var target = new PlayerSession(2, "target", "target-token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetById(2).Returns(target);
        var reader = new BanchoPacketReader(PacketWriter.WriteI32List([2]));

        await new UserPresenceRequestHandler(_sessionRegistry).HandleAsync(self, reader);

        Assert.Equal(ServerPacketWriter.UserPresence(2, "target", 0, 0, (int)ClientPrivileges.Player, 0, 0.0, 0.0, 0), self.Dequeue());
    }

    [Fact]
    public async Task Handle_UnknownTarget_NothingEnqueued()
    {
        var self = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetById(2).Returns((PlayerSession?)null);
        var reader = new BanchoPacketReader(PacketWriter.WriteI32List([2]));

        await new UserPresenceRequestHandler(_sessionRegistry).HandleAsync(self, reader);

        Assert.Empty(self.Dequeue());
    }
}
