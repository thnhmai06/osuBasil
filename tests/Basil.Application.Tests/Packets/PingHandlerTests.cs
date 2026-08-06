using Basil.Application.Packets.Users;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;

namespace Basil.Application.Tests.Packets;

/// <summary>Ported from app/api/domains/cho.py's Ping (@register(ClientPackets.PING, restricted=True)) — a no-op.</summary>
public class PingHandlerTests
{
	[Fact]
	public void PacketId_IsPing()
	{
		Assert.Equal(ClientPackets.Ping, new PingHandler().PacketId);
	}

	[Fact]
	public void AllowedWhenRestricted_IsTrue()
	{
		Assert.True(new PingHandler().AllowedWhenRestricted);
	}

	[Fact]
	public async Task Handle_DoesNothing()
	{
		var session = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var reader = new PacketReader(Array.Empty<byte>());

		await new PingHandler().HandleAsync(session, reader);

		Assert.Empty(session.Dequeue());
	}
}