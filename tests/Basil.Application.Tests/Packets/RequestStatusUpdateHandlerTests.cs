using Basil.Application.Packets.Users;
using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Packets;

namespace Basil.Application.Tests.Packets;

/// <summary>Ported from app/api/domains/cho.py's StatsUpdateRequest (@register(ClientPackets.REQUEST_STATUS_UPDATE)).</summary>
public class RequestStatusUpdateHandlerTests
{
	[Fact]
	public async Task Handle_EnqueuesOwnUserStatsPacket()
	{
		var session = new GameSession(42, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			ModeStats =
			{
				[GameMode.Standard] = new CachedPlayerStats(1000, 900, 10, 3)
			}
		};
		var reader = new PacketReader(Array.Empty<byte>());

		await new RequestStatusUpdateHandler().HandleAsync(session, reader);

		var expected = ServerPacketWriter.UserStats(
			42, (int)UserActivity.Idle, "", "", (int)Mods.NoMod, 0, 0, 900, 100.0, 10, 1000, 3, 727);
		Assert.Equal(expected, session.Dequeue());
	}
}