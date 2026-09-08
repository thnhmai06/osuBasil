using Basil.Server.Features.Multiplayer.Packets;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;
using static Basil.Server.Tests.Features.Multiplayer.Packets.MultiplayerTestSupport;

namespace Basil.Server.Tests.Features.Multiplayer.Packets;

/// <summary>Verifies the `MatchHasBeatmap` handler sets the player's slot to NotReady.</summary>
public class MatchHasBeatmapHandlerTests
{
	[Fact]
	public async Task Handle_SetsSlotStatusToNotReady()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);
		match.Slots[0].Status = SlotStatus.NoMap;
		var handler = new MatchHasBeatmapHandler();

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Equal(SlotStatus.NotReady, match.GetSlot(host.Id)!.Status);
	}
}