using Basil.Application.Packets.Multiplayer;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;
using static Basil.Application.Tests.Packets.MultiplayerTestSupport;

namespace Basil.Application.Tests.Packets;

/// <summary>Verifies the `MatchReady` handler sets the player's slot to Ready.</summary>
public class MatchReadyHandlerTests
{
	[Fact]
	public async Task Handle_InMatch_SetsSlotReady()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);
		var handler = new MatchReadyHandler(fixture.MatchMembership);

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Equal(SlotStatus.Ready, match.GetSlot(host.Id)!.Status);
	}

	[Fact]
	public async Task Handle_NotInAMatch_NoOp()
	{
		var fixture = new Fixture();
		var player = MakePlayer(1, "alice");
		var handler = new MatchReadyHandler(fixture.MatchMembership);

		await handler.HandleAsync(player, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Empty(player.Dequeue());
	}
}