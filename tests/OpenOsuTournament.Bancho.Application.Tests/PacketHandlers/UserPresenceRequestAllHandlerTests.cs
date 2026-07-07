using NSubstitute;
using OpenOsuTournament.Bancho.Application.PacketHandlers.Core;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Domain.Users;
using OpenOsuTournament.Bancho.Protocol.Packets;

namespace OpenOsuTournament.Bancho.Application.Tests.PacketHandlers;

/// <summary>
///     Ported from app/api/domains/cho.py's UserPresenceRequestAll
///     (@register(ClientPackets.USER_PRESENCE_REQUEST_ALL)).
/// </summary>
public class UserPresenceRequestAllHandlerTests
{
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    [Fact]
    public async Task Handle_EnqueuesPresenceOfAllUnrestrictedPlayers_ExcludingRestricted()
    {
        var self = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var unrestrictedOther = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        var restrictedOther = new PlayerSession(3, "banned", "banned-token", Privileges.Verified, 0.0);
        _sessionRegistry.All.Returns([self, unrestrictedOther, restrictedOther]);
        var reader = new BanchoPacketReader(PacketWriter.WriteInt32(0));

        await new UserPresenceRequestAllHandler(_sessionRegistry).HandleAsync(self, reader);

        var expected = ServerPacketWriter.UserPresence(1, "cmyui", 0, 0, (int)ClientPrivileges.Player, 0, 0.0, 0.0, 0)
            .Concat(ServerPacketWriter.UserPresence(2, "other", 0, 0, (int)ClientPrivileges.Player, 0, 0.0, 0.0, 0))
            .ToArray();
        Assert.Equal(expected, self.Dequeue());
    }
}