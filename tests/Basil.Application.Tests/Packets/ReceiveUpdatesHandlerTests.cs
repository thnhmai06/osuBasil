using Basil.Application.Packets.Users;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

/// <summary>Ported from app/api/domains/cho.py's ReceiveUpdates.</summary>
public class ReceiveUpdatesHandlerTests
{
	private static GameSession MakeSession()
	{
		return new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
	}

	[Theory]
	[InlineData(0, PresenceFilter.Nil)]
	[InlineData(1, PresenceFilter.All)]
	[InlineData(2, PresenceFilter.Friends)]
	public async Task Handle_ValidValue_UpdatesPresenceFilter(int value, PresenceFilter expected)
	{
		var session = MakeSession();
		var reader = new PacketReader(BinaryWriter.WriteInt32(value));

		await new ReceiveUpdatesHandler().HandleAsync(session, reader);

		Assert.Equal(expected, session.PresenceFilter);
	}

	[Fact]
	public async Task Handle_OutOfRangeValue_IgnoredKeepingPreviousFilter()
	{
		var session = MakeSession();
		session.PresenceFilter = PresenceFilter.Friends;
		var reader = new PacketReader(BinaryWriter.WriteInt32(99));

		await new ReceiveUpdatesHandler().HandleAsync(session, reader);

		Assert.Equal(PresenceFilter.Friends, session.PresenceFilter);
	}
}