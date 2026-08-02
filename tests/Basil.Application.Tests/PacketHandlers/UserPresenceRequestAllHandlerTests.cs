using Basil.Application.Packets.Users;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>
///     Ported from app/api/domains/cho.py's UserPresenceRequestAll
///     (@register(ClientPackets.USER_PRESENCE_REQUEST_ALL)).
/// </summary>
public class UserPresenceRequestAllHandlerTests
{
	private readonly IUserSessionRegistry _sessionRegistry = Substitute.For<IUserSessionRegistry>();

	[Fact]
	public async Task Handle_EnqueuesPresenceOfAllUnrestrictedPlayers_ExcludingRestricted()
	{
		var self = new UserSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var unrestrictedOther = new UserSession(2, "other", "other-token", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch);
		var restrictedOther =
			new UserSession(3, "banned", "banned-token", UserPrivileges.Verified, DateTimeOffset.UnixEpoch);
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