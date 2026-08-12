using Basil.Application.Packets.Multiplayer;
using Basil.Protocol.Packets;
using static Basil.Application.Tests.Packets.MultiplayerTestSupport;

namespace Basil.Application.Tests.Packets;

/// <summary>
///     Verifies the `MatchScoreUpdate` handler forwards the raw score frame (with the slot id injected) and publishes
///     the player's live score.
/// </summary>
public class MatchScoreUpdateHandlerTests
{
	[Fact]
	public async Task Handle_ForwardsRawFrameWithSlotIdInjectedAtByteEleven()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		fixture.RegisterAll(host, guest);
		var match = fixture.CreateMatch(host);
		await fixture.MatchMembership.JoinAsync(guest, match, "");
		host.Dequeue();
		var handler = new MatchScoreUpdateHandler(fixture.MatchMembership, fixture.EventBus);
		var frame = new byte[] { 1, 2, 3, 4, 5, 6 };

		await handler.HandleAsync(guest, new PacketReader(frame));

		var forwarded = Chunk(host.Dequeue()).Single();
		Assert.Equal((int)ServerPackets.MatchScoreUpdate, BitConverter.ToUInt16(forwarded, 0));
		Assert.Equal(1, forwarded[11]); // guest's slot id (1) overwrites byte 11 (frame[4])
		var expectedBody = (byte[])frame.Clone();
		expectedBody[4] = 1;
		Assert.Equal(expectedBody, forwarded[7..]);
	}

	[Fact]
	public async Task Handle_TooShortToBeAScoreFrame_StillForwardsRelay_JustSkipsThePublish()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		fixture.RegisterAll(host, guest);
		var match = fixture.CreateMatch(host);
		await fixture.MatchMembership.JoinAsync(guest, match, "");
		var handler = new MatchScoreUpdateHandler(fixture.MatchMembership, fixture.EventBus);

		await handler.HandleAsync(guest, new PacketReader(new byte[] { 1, 2, 3, 4, 5, 6 }));

		Assert.Empty(fixture.EventBus.PlayerPublishes);
	}

	[Fact]
	public async Task Handle_ValidScoreFrame_PublishesPlayerLiveScore()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		fixture.RegisterAll(host, guest);
		var match = fixture.CreateMatch(host);
		await fixture.MatchMembership.JoinAsync(guest, match, "");
		var handler = new MatchScoreUpdateHandler(fixture.MatchMembership, fixture.EventBus);

		// Matches SCOREFRAME_FMT = "<iBHHHHHHiHH?BB?" (29 bytes), scoreV2 = false (last byte 0).
		var frame = new byte[29];
		BitConverter.GetBytes(12345).CopyTo(frame, 0); // time
		frame[4] = 0; // id (placeholder, overwritten server-side)
		BitConverter.GetBytes((ushort)100).CopyTo(frame, 5); // num300
		BitConverter.GetBytes(500_000).CopyTo(frame, 17); // totalScore (int, 4 bytes at offset 17)

		await handler.HandleAsync(guest, new PacketReader(frame));

		var publish = Assert.Single(fixture.EventBus.PlayerPublishes);
		Assert.Equal(match.DbId, publish.MatchDbId);
		Assert.Equal("guest", publish.PlayerName);
	}
}