using Basil.Application.Packets.Users;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

/// <summary>Ported from app/api/domains/cho.py's UserPresenceRequest (@register(ClientPackets.USER_PRESENCE_REQUEST)).</summary>
public class UserPresenceRequestHandlerTests
{
	private readonly ISessionRegistry<GameSession> _sessionRegistry = Substitute.For<ISessionRegistry<GameSession>>();

	[Fact]
	public async Task Handle_KnownTarget_EnqueuesTheirPresence()
	{
		var self = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var target = new GameSession(2, "target", "target-token", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch);
		_sessionRegistry.GetByUserId(2).Returns(target);
		var reader = new PacketReader(BinaryWriter.WriteI32List([2]));

		await new UserPresenceRequestHandler(_sessionRegistry).HandleAsync(self, reader);

		// countryCode 244 = CountryCode.Xx (unset default) — the enum has no 0 member.
		Assert.Equal(
			ServerPacketWriter.UserPresence(2, "target", 0, 244, (int)ClientPrivileges.Player, 0, 0.0, 0.0, 0),
			self.Dequeue());
	}

	[Fact]
	public async Task Handle_UnknownTarget_NothingEnqueued()
	{
		var self = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_sessionRegistry.GetByUserId(2).Returns((GameSession?)null);
		var reader = new PacketReader(BinaryWriter.WriteI32List([2]));

		await new UserPresenceRequestHandler(_sessionRegistry).HandleAsync(self, reader);

		Assert.Empty(self.Dequeue());
	}
}