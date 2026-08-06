using Basil.Application.Packets.Multiplayer;
using Basil.Protocol.Packets;
using static Basil.Application.Tests.Packets.MultiplayerTestSupport;

namespace Basil.Application.Tests.Packets;

/// <summary>Ported from app/api/domains/cho.py's MatchPart.</summary>
public class PartMatchHandlerTests
{
	[Fact]
	public async Task Handle_NotInAMatch_NoOp()
	{
		var fixture = new Fixture();
		var player = MakePlayer(1, "alice");
		var handler = new PartMatchHandler(fixture.MatchMembership);

		await handler.HandleAsync(player, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Null(player.Match);
	}

	[Fact]
	public async Task Handle_InAMatch_LeavesAndClearsPlayerMatch()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);
		var handler = new PartMatchHandler(fixture.MatchMembership);

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Null(host.Match);
		// The room no longer tears down the instant it's empty — it starts a 5-minute
		// auto-close timer instead (see MatchMembershipService.SyncEmptyRoomTimer).
		Assert.NotNull(fixture.MatchRegistry.GetById(match.Id));
		Assert.NotNull(match.EmptyRoomTimer);
	}
}