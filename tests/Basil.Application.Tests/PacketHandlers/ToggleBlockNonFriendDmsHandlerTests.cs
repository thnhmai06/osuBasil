using Basil.Application.PacketHandlers.Channels;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's ToggleBlockingDMs.</summary>
public class ToggleBlockNonFriendDmsHandlerTests
{
    private static BanchoPacketReader ValueReader(int value)
    {
        return new BanchoPacketReader(PacketWriter.WriteInt32(value));
    }

    [Fact]
    public async Task HandleAsync_ValueOne_SetsPmPrivateTrue()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

        await new ToggleBlockNonFriendDmsHandler().HandleAsync(player, ValueReader(1));

        Assert.True(player.PmPrivate);
    }

    [Fact]
    public async Task HandleAsync_ValueZero_SetsPmPrivateFalse()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0) { PmPrivate = true };

        await new ToggleBlockNonFriendDmsHandler().HandleAsync(player, ValueReader(0));

        Assert.False(player.PmPrivate);
    }
}