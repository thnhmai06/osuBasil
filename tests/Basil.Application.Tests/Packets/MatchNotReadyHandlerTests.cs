using Basil.Application.Packets.Multiplayer;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;
using static Basil.Application.Tests.Packets.MultiplayerTestSupport;

namespace Basil.Application.Tests.Packets;

/// <summary>Ported from app/api/domains/cho.py's MatchNotReady.</summary>
public class MatchNotReadyHandlerTests
{
	[Fact]
	public async Task Handle_SetsSlotStatusToNotReady()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);
		match.Slots[0].Status = SlotStatus.Ready;
		var handler = new MatchNotReadyHandler(fixture.MatchMembership);

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Equal(SlotStatus.NotReady, match.GetSlot(host.Id)!.Status);
	}
}