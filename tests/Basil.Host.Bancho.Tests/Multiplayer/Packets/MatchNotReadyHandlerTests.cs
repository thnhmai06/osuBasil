using Basil.Application.Users;
using Basil.Domain.Multiplayer;
using Basil.Host.Bancho.Multiplayer.Packets;
using Basil.Protocol.Packets;
using static Basil.Infrastructure.Tests.Multiplayer.Packets.MultiplayerTestSupport;

namespace Basil.Host.Bancho.Tests.Multiplayer.Packets;

/// <summary>Verifies the `MatchNotReady` handler sets the player's slot to NotReady.</summary>
public class MatchNotReadyHandlerTests
{
	[Fact]
	public async Task Handle_SetsSlotStatusToNotReady()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);
		match.Slots[0].Status = RoomSlotStatus.Ready;
		var handler = new MatchNotReadyHandler(fixture.UserCache);

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Equal(RoomSlotStatus.NotReady, match.GetSlot(fixture.UserCache.Resolve(host))!.Status);
	}
}