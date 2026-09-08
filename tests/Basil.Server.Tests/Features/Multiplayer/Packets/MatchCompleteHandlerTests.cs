using Basil.Server.Features.Multiplayer.Packets;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using static Basil.Server.Tests.Features.Multiplayer.Packets.MultiplayerTestSupport;

namespace Basil.Server.Tests.Features.Multiplayer.Packets;

/// <summary>
///     Verifies the `MatchComplete` handler: marks the completing player's slot complete and
///     finishes the match once every playing player has completed.
/// </summary>
public class MatchCompleteHandlerTests
{
	[Fact]
	public async Task Handle_OtherPlayerStillPlaying_DoesNotFinishMatch()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		fixture.RegisterAll(host, guest);
		var match = fixture.CreateMatch(host);
		await fixture.MatchMembership.JoinAsync(guest, match, "");
		match.Slots[0].Status = SlotStatus.Playing;
		match.Slots[1].Status = SlotStatus.Playing;
		match.InProgress = true;
		host.Dequeue();
		guest.Dequeue();
		var handler = new MatchCompleteHandler(fixture.MatchMembership, fixture.RoundEndOutbox,
			NullLogger<MatchCompleteHandler>.Instance);

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Equal(SlotStatus.Complete, match.Slots[0].Status);
		Assert.True(match.InProgress);
		Assert.Empty(host.Dequeue());
	}

	[Fact]
	public async Task Handle_EveryoneDone_FinishesMatchAndBroadcastsCompleteImmuneToNonPlayers()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		var spectatorish = MakePlayer(3, "afk"); // in the room but never played (status stays Open/NotReady)
		fixture.RegisterAll(host, guest, spectatorish);
		var match = fixture.CreateMatch(host);
		await fixture.MatchMembership.JoinAsync(guest, match, "");
		await fixture.MatchMembership.JoinAsync(spectatorish, match, "");
		match.Slots[0].Status = SlotStatus.Playing;
		match.Slots[1].Status = SlotStatus.Playing;
		match.Slots[2].Status = SlotStatus.NotReady; // never started playing
		match.InProgress = true;
		match.Slots[1].Loaded = true;
		host.Dequeue();
		guest.Dequeue();
		spectatorish.Dequeue();
		var handler = new MatchCompleteHandler(fixture.MatchMembership, fixture.RoundEndOutbox,
			NullLogger<MatchCompleteHandler>.Instance);

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));
		await handler.HandleAsync(guest, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.False(match.InProgress);
		Assert.False(match.Slots[1].Loaded);
		Assert.Contains(ServerPacketWriter.MatchComplete(), Chunk(host.Dequeue()));
		// immune from match_complete itself (still gets the enqueue_state update, just not this packet)
		Assert.DoesNotContain(ServerPacketWriter.MatchComplete(), Chunk(spectatorish.Dequeue()));
	}

	/// <summary>
	///     Regression test (ADR-003): a full round-end outbox must not stop the room from seeing a
	///     consistent match-complete broadcast — only the database write is lost, loudly logged, not
	///     the in-memory state transition or the packets players actually see.
	/// </summary>
	[Fact]
	public async Task Handle_OutboxFull_StillFinishesMatchAndBroadcasts()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);
		match.Slots[0].Status = SlotStatus.Playing;
		match.InProgress = true;
		match.CurrentRoundId = 1;
		fixture.RoundEndOutbox.ThrowFull = true;
		host.Dequeue();
		var handler = new MatchCompleteHandler(fixture.MatchMembership, fixture.RoundEndOutbox,
			NullLogger<MatchCompleteHandler>.Instance);

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.False(match.InProgress);
		Assert.Contains(ServerPacketWriter.MatchComplete(), Chunk(host.Dequeue()));
	}

	/// <summary>
	///     Regression test: a duplicate or late-arriving completion for a round that already closed
	///     must not re-enqueue that round's end a second time. Every slot stays <c>Complete</c> once a
	///     round closes (nothing resets it until the next start), so without an explicit
	///     <c>InProgress</c> check the "no slot still playing" guard alone would let a second
	///     completion straight through.
	/// </summary>
	[Fact]
	public async Task Handle_CompletionAfterRoundAlreadyClosed_DoesNotReEnqueue()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);
		match.Slots[0].Status = SlotStatus.Complete;
		match.InProgress = false;
		match.CurrentRoundId = 1;
		var handler = new MatchCompleteHandler(fixture.MatchMembership, fixture.RoundEndOutbox,
			NullLogger<MatchCompleteHandler>.Instance);

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));

		Assert.Empty(fixture.RoundEndOutbox.Enqueued);
	}
}