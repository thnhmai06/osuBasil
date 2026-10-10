using Basil.Domain.Multiplayer;
using Basil.Host.Bancho.Multiplayer.Packets;
using Basil.Protocol.Packets;
using static Basil.Infrastructure.Tests.Multiplayer.Packets.MultiplayerTestSupport;

namespace Basil.Host.Bancho.Tests.Multiplayer.Packets;

/// <summary>Verifies the `MatchLoadComplete` handler broadcasts all-players-loaded once every playing slot has loaded.</summary>
public class MatchLoadCompleteHandlerTests
{
	[Fact]
	public async Task Handle_NotAllPlayingSlotsLoaded_DoesNotBroadcastAllLoaded()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		fixture.RegisterAll(host, guest);
		var match = fixture.CreateMatch(host);
		await fixture.MatchMembership.JoinAsync(guest, match, "");
		match.Slots[0].Status = RoomSlotStatus.Playing;
		match.Slots[1].Status = RoomSlotStatus.Playing;
		host.Dequeue();
		guest.Dequeue();
		var handler = new MatchLoadCompleteHandler(fixture.MatchBroadcast, fixture.UserCache);

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.True(match.Slots[0].BeatmapLoaded);
		Assert.Empty(host.Dequeue());
	}

	[Fact]
	public async Task Handle_AllPlayingSlotsLoaded_BroadcastsAllPlayersLoaded()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		fixture.RegisterAll(host, guest);
		var match = fixture.CreateMatch(host);
		await fixture.MatchMembership.JoinAsync(guest, match, "");
		match.Slots[0].Status = RoomSlotStatus.Playing;
		match.Slots[1].Status = RoomSlotStatus.Playing;
		match.Slots[1].BeatmapLoaded = true;
		host.Dequeue();
		guest.Dequeue();
		var handler = new MatchLoadCompleteHandler(fixture.MatchBroadcast, fixture.UserCache);

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Contains(ServerPacketWriter.MatchAllPlayersLoaded(), Chunk(host.Dequeue()));
	}
}