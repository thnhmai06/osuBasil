using Basil.Application.Packets.Users;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

/// <summary>
///     Verifies the `UserPresenceRequestAll` handler: enqueues presence for every unrestricted
///     player, excluding restricted players.
/// </summary>
public class UserPresenceRequestAllHandlerTests
{
	private readonly ISessionRegistry<GameSession> _sessionRegistry = Substitute.For<ISessionRegistry<GameSession>>();

	[Fact]
	public async Task Handle_EnqueuesPresenceOfAllUnrestrictedPlayers_ExcludingRestricted()
	{
		var self = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var unrestrictedOther = new GameSession(2, "other", "other-token", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch);
		var restrictedOther =
			new GameSession(3, "banned", "banned-token", UserPrivileges.Verified, DateTimeOffset.UnixEpoch);
		_sessionRegistry.All.Returns([self, unrestrictedOther, restrictedOther]);
		var reader = new PacketReader(BinaryWriter.WriteInt32(0));

		await new UserPresenceRequestAllHandler(_sessionRegistry).HandleAsync(self, reader);

		// countryCode 244 = CountryCode.Xx (unset default) — the enum has no 0 member.
		var expected =
			ServerPacketWriter.UserPresence(1, "cmyui", 0, 244, (int)ClientPrivileges.Player, 0, 0.0, 0.0, 0)
				.Concat(ServerPacketWriter.UserPresence(2, "other", 0, 244, (int)ClientPrivileges.Player, 0, 0.0, 0.0,
					0))
				.ToArray();
		Assert.Equal(expected, self.Dequeue());
	}
}