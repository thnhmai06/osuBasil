using Basil.Application.Packets.Multiplayer;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;
using static Basil.Application.Tests.Packets.MultiplayerTestSupport;

namespace Basil.Application.Tests.Packets;

/// <summary>
///     Verifies the `MatchStart` handler starts the match, host-only, starting players with the map and skipping
///     players without it.
/// </summary>
public class MatchStartHandlerTests
{
	[Fact]
	public async Task Handle_NonHost_NoOp()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		fixture.RegisterAll(host, guest);
		var match = fixture.CreateMatch(host);
		await fixture.MatchMembership.JoinAsync(guest, match, "");
		var handler = new MatchStartHandler(fixture.MatchMembership);

		await handler.HandleAsync(guest, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.False(match.InProgress);
	}

	[Fact]
	public async Task Handle_Host_StartsPlayersWithMapAndSkipsPlayersWithoutIt()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		fixture.RegisterAll(host, guest);
		var match = fixture.CreateMatch(host);
		await fixture.MatchMembership.JoinAsync(guest, match, "");
		match.Slots[1].Status = SlotStatus.NoMap;
		var handler = new MatchStartHandler(fixture.MatchMembership);

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.True(match.InProgress);
		Assert.Equal(SlotStatus.Playing, match.Slots[0].Status);
		Assert.Equal(SlotStatus.NoMap, match.Slots[1].Status);
	}

	/// <summary>
	///     TOCTOU regression: host status is read once before the lock (to short-circuit non-hosts
	///     cheaply) and must be re-checked after acquiring it, since host can only change under the
	///     same lock. A sender who was host when the packet arrived but lost host while waiting for
	///     the lock must not still be treated as authoritative once it's acquired.
	/// </summary>
	[Fact]
	public async Task Handle_HostChangesWhileWaitingForLock_NoOp()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		fixture.RegisterAll(host, guest);
		var match = fixture.CreateMatch(host);
		await fixture.MatchMembership.JoinAsync(guest, match, "");
		var handler = new MatchStartHandler(fixture.MatchMembership);

		await match.Lock.WaitAsync();
		var handleTask = handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));
		match.HostId = guest.Id;
		match.Lock.Release();
		await handleTask;

		Assert.False(match.InProgress);
	}
}