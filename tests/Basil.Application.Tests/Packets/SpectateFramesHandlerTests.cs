using Basil.Application.Packets.Spectating;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;

namespace Basil.Application.Tests.Packets;

/// <summary>
///     Verifies the `SpectateFrames` handler: forwards raw bytes unparsed to native spectators and
///     decodes the same bytes into a structured SSE payload.
/// </summary>
public class SpectateFramesHandlerTests
{
	/// <summary>
	///     A minimal but well-formed SpectateFrames bundle (extra=0, 0 frames, action=Standard, an
	///     all-zero non-scorev2 scoreframe, sequence=0) — see BanchoPacketReaderTests in
	///     Basil.Protocol.Tests for the full wire-format round-trip coverage; this fixture only needs
	///     to be parseable, not meaningful, since these tests assert on publish behavior, not payload
	///     content.
	/// </summary>
	private static readonly byte[] ValidBundleBytes =
		Convert.FromHexString("0000000000000000000000000000000000000000000000000000000000000000000000000000");

	private static GameSession MakePlayer(int id, string name)
	{
		return new GameSession(id, name, "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
	}

	[Fact]
	public async Task Handle_ForwardsRawBytesToAllSpectators()
	{
		var host = MakePlayer(1, "host");
		var spectator1 = MakePlayer(2, "alice");
		var spectator2 = MakePlayer(3, "bob");
		host.AddSpectator(spectator1);
		host.AddSpectator(spectator2);
		var rawFrameData = new byte[] { 1, 2, 3, 4, 5 };
		var playerInputEvents = new MultiplayerTestSupport.FakePlayerInputEvents();

		await new SpectateFramesHandler(playerInputEvents).HandleAsync(host, new PacketReader(rawFrameData));

		var expected = ServerPacketWriter.SpectateFrames(rawFrameData);
		Assert.Equal(expected, spectator1.Dequeue());
		Assert.Equal(expected, spectator2.Dequeue());
	}

	[Fact]
	public async Task Handle_NoSpectators_NoOp()
	{
		var host = MakePlayer(1, "host");
		var playerInputEvents = new MultiplayerTestSupport.FakePlayerInputEvents();

		await new SpectateFramesHandler(playerInputEvents).HandleAsync(host, new PacketReader(new byte[] { 1 }));

		Assert.Empty(host.Dequeue());
	}

	[Fact]
	public async Task Handle_SpectatedPlayerNotInAMatch_StillPublishesInputFrameByPlayerId()
	{
		var host = MakePlayer(1, "host");
		var playerInputEvents = new MultiplayerTestSupport.FakePlayerInputEvents();

		await new SpectateFramesHandler(playerInputEvents).HandleAsync(host, new PacketReader(ValidBundleBytes));

		var publish = Assert.Single(playerInputEvents.Publishes);
		Assert.Equal(host.Id, publish.PlayerId);
	}

	[Fact]
	public async Task Handle_SpectatedPlayerInAMatch_AlsoPublishesInputFrameByPlayerId()
	{
		var host = MakePlayer(1, "host");
		var fixture = new MultiplayerTestSupport.Fixture();
		fixture.RegisterAll(host);
		await fixture.MatchMembership.CreateAsync(host, MultiplayerTestSupport.MakeMatchData(host.Id));
		var playerInputEvents = new MultiplayerTestSupport.FakePlayerInputEvents();

		await new SpectateFramesHandler(playerInputEvents).HandleAsync(host, new PacketReader(ValidBundleBytes));

		var publish = Assert.Single(playerInputEvents.Publishes);
		Assert.Equal(host.Id, publish.PlayerId);
	}

	[Fact]
	public async Task Handle_MalformedBundle_StillForwardsRawBytesButSkipsPublish()
	{
		var host = MakePlayer(1, "host");
		var spectator = MakePlayer(2, "alice");
		host.AddSpectator(spectator);
		var playerInputEvents = new MultiplayerTestSupport.FakePlayerInputEvents();
		var tooShortToDecode = new byte[] { 1, 2, 3 };

		await new SpectateFramesHandler(playerInputEvents).HandleAsync(host,
			new PacketReader(tooShortToDecode));

		Assert.Equal(ServerPacketWriter.SpectateFrames(tooShortToDecode), spectator.Dequeue());
		Assert.Empty(playerInputEvents.Publishes);
	}
}