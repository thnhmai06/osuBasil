using Basil.Application.Packets.Multiplayer;
using Basil.Protocol.Packets;
using static Basil.Application.Tests.Packets.MultiplayerTestSupport;

namespace Basil.Application.Tests.Packets;

/// <summary>Ported from app/api/domains/cho.py's MatchFailed.</summary>
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
		var handler = new MatchFailedHandler(fixture.MatchMembership);

		await handler.HandleAsync(guest, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Contains(ServerPacketWriter.MatchPlayerFailed(1), Chunk(host.Dequeue()));
	}
}