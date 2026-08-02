using Basil.Application.Packets.Users;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;

namespace Basil.Application.Tests.Packets;

/// <summary>Ported from app/api/domains/cho.py's SetAwayMessage.</summary>
public class SetAwayMessageHandlerTests
{
	[Fact]
	public async Task Handle_SetsAwayMessageFromMessageText()
	{
		var session = new UserSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var payload =
			ServerPacketWriter.SendMessage("cmyui", "gone fishing", "", 1)
				[7..]; // strip packet header, keep message payload
		var reader = new PacketReader(payload);

		await new SetAwayMessageHandler().HandleAsync(session, reader);

		Assert.Equal("gone fishing", session.AwayMessage);
	}
}