using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Host.Bancho.Users.Packets;
using Basil.Protocol.Packets;

namespace Basil.Host.Bancho.Tests.Users.Packets;

/// <summary>Verifies the `SetAwayMessage` handler updates the player's away message.</summary>
public class SetAwayMessageHandlerTests
{
	[Fact]
	public async Task Handle_SetsAwayMessageFromMessageText()
	{
		var session = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var payload =
			ServerPacketWriter.SendMessage("cmyui", "gone fishing", "", 1)
				[7..]; // strip packet header, keep message payload
		var reader = new PacketReader(payload);

		await new SetAwayMessageHandler().HandleAsync(session, reader);

		Assert.Equal("gone fishing", session.AwayMessage);
	}
}