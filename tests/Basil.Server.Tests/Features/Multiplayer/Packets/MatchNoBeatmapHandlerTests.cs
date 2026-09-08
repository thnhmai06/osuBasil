using Basil.Server.Features.Multiplayer.Packets;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;
using static Basil.Server.Tests.Features.Multiplayer.Packets.MultiplayerTestSupport;

namespace Basil.Server.Tests.Features.Multiplayer.Packets;

/// <summary>Verifies the `MatchNoBeatmap` handler sets the player's slot to NoMap.</summary>
public class MatchNoBeatmapHandlerTests
{
	[Fact]
	public async Task Handle_SetsSlotStatusToNoMap()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);
		var handler = new MatchNoBeatmapHandler();

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Equal(SlotStatus.NoMap, match.GetSlot(host.Id)!.Status);
	}
}