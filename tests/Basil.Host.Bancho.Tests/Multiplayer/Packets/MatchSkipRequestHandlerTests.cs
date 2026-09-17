using Basil.Domain.Multiplayer;
using Basil.Host.Bancho.Multiplayer.Packets;
using Basil.Protocol.Packets;
using static Basil.Infrastructure.Tests.Multiplayer.Packets.MultiplayerTestSupport;

namespace Basil.Host.Bancho.Tests.Multiplayer.Packets;

/// <summary>Verifies the `MatchSkipRequest` handler broadcasts a skip once every playing player has skipped.</summary>
public class MatchSkipRequestHandlerTests
{
	[Fact]
	public async Task Handle_NotEveryonePlayingHasSkipped_DoesNotBroadcastSkip()
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
		var handler = new MatchSkipRequestHandler(fixture.MatchBroadcast, fixture.UserCache);

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.True(match.Slots[0].IntroSkipped);
		Assert.Contains(ServerPacketWriter.MatchPlayerSkipped(host.Id), Chunk(host.Dequeue()));
		Assert.DoesNotContain(ServerPacketWriter.MatchSkip(), Chunk(guest.Dequeue()));
	}

	[Fact]
	public async Task Handle_EveryonePlayingHasSkipped_BroadcastsSkip()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		fixture.RegisterAll(host, guest);
		var match = fixture.CreateMatch(host);
		await fixture.MatchMembership.JoinAsync(guest, match, "");
		match.Slots[0].Status = RoomSlotStatus.Playing;
		match.Slots[1].Status = RoomSlotStatus.Playing;
		match.Slots[1].IntroSkipped = true;
		host.Dequeue();
		guest.Dequeue();
		var handler = new MatchSkipRequestHandler(fixture.MatchBroadcast, fixture.UserCache);

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Contains(ServerPacketWriter.MatchSkip(), Chunk(host.Dequeue()));
	}
}