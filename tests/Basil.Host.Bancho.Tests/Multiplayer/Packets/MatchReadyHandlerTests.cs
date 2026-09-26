using Basil.Application.Users;
using Basil.Domain.Multiplayer;
using Basil.Host.Bancho.Multiplayer.Packets;
using Basil.Protocol.Packets;
using NSubstitute;
using static Basil.Infrastructure.Tests.Multiplayer.Packets.MultiplayerTestSupport;

namespace Basil.Host.Bancho.Tests.Multiplayer.Packets;

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
		var handler = new MatchReadyHandler(fixture.UserCache);

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Equal(RoomSlotStatus.Ready, match.GetSlot(fixture.UserCache.Resolve(host))!.Status);
	}

	[Fact]
	public async Task Handle_NotInAMatch_NoOp()
	{
		var player = MakePlayer(1, "alice");
		var handler = new MatchReadyHandler(Substitute.For<IUserCache>());

		await handler.HandleAsync(player, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Empty(player.Dequeue());
	}
}