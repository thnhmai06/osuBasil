using Basil.Application.Packets.Channels;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

/// <summary>Ported from app/api/domains/cho.py's ToggleBlockingDMs.</summary>
public class ToggleBlockNonFriendDmsHandlerTests
{
	private static PacketReader ValueReader(int value)
	{
		return new PacketReader(BinaryWriter.WriteInt32(value));
	}

	[Fact]
	public async Task HandleAsync_ValueOne_SetsPmPrivateTrue()
	{
		var player = new UserSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);

		await new ToggleBlockNonFriendDmsHandler().HandleAsync(player, ValueReader(1));

		Assert.True(player.PmPrivate);
	}

	[Fact]
	public async Task HandleAsync_ValueZero_SetsPmPrivateFalse()
	{
		var player = new UserSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
			{ PmPrivate = true };

		await new ToggleBlockNonFriendDmsHandler().HandleAsync(player, ValueReader(0));

		Assert.False(player.PmPrivate);
	}
}