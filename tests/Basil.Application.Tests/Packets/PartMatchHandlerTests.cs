using Basil.Application.Packets.Multiplayer;
using Basil.Protocol.Packets;
using static Basil.Application.Tests.Packets.MultiplayerTestSupport;

namespace Basil.Application.Tests.Packets;

/// <summary>Verifies the `MatchPart` handler removes the player from the match.</summary>
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

	/// <summary>
	///     Regression test for the ADR-004 4b follow-up: LeaveAsync no longer publishes internally, so
	///     the handler itself must publish after releasing the lock -- a missed call here would leave
	///     SSE subscribers watching a leave that never broadcasts.
	/// </summary>
	[Fact]
	public async Task Handle_InAMatch_PublishesUpdatedMainSnapshot()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);
		var beforeLeave = match.MainSnapshot.Latest;
		var handler = new PartMatchHandler(fixture.MatchMembership);

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.NotNull(match.MainSnapshot.Latest);
		Assert.NotSame(beforeLeave, match.MainSnapshot.Latest);
	}
}