using Basil.Server.Features.Multiplayer.Packets;
using Basil.Protocol.Packets;
using static Basil.Server.Tests.Features.Multiplayer.Packets.MultiplayerTestSupport;

namespace Basil.Server.Tests.Features.Multiplayer.Packets;

/// <summary>Verifies the `MatchFailed` handler broadcasts the player's failed slot.</summary>
public class MatchFailedHandlerTests
{
	[Fact]
	public async Task Handle_BroadcastsPlayerFailedWithCorrectSlotId()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		fixture.RegisterAll(host, guest);
		var match = fixture.CreateMatch(host);
		await fixture.MatchMembership.JoinAsync(guest, match, "");
		host.Dequeue();
		var handler = new MatchFailedHandler(fixture.MatchBroadcast);

		await handler.HandleAsync(guest, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Contains(ServerPacketWriter.MatchPlayerFailed(1), Chunk(host.Dequeue()));
	}
}