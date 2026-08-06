using System.Buffers.Binary;
using Basil.Application.Packets;
using Basil.Application.Packets.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using static Basil.Application.Tests.Packets.MultiplayerTestSupport;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

/// <summary>
///     Ported from app/state/__init__.py's packet_map ("all"/"restricted" split) + the dispatch loop
///     in app/api/domains/cho.py's bancho_handler (`for packet in PacketReader(...): await
///     packet.handle(userSession)` — unhandled packet types are skipped via their declared length).
/// </summary>
public class PacketDispatcherTests
{
	private static byte[] PacketBytes(ClientPackets type, byte[] payload)
	{
		var header = new byte[7];
		BitConverterHeader(header, type, payload.Length);
		return [.. header, .. payload];
	}

	private static void BitConverterHeader(byte[] header, ClientPackets type, int length)
	{
		BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)type);
		BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(3), length);
	}

	[Fact]
	public async Task Dispatch_KnownPacket_InvokesHandlerWithReaderPositionedAfterHeader()
	{
		var called = false;
		var handler = new FakeHandler(ClientPackets.Ping, true, (_, _) => called = true);
		var dispatcher = new PacketDispatcher([handler], NullLogger<PacketDispatcher>.Instance);
		var session = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);

		await dispatcher.DispatchAsync(session, PacketBytes(ClientPackets.Ping, []));

		Assert.True(called);
	}

	[Fact]
	public async Task Dispatch_UnknownPacket_SkippedWithoutError()
	{
		var dispatcher = new PacketDispatcher([], NullLogger<PacketDispatcher>.Instance);
		var session = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);

		await dispatcher.DispatchAsync(session, PacketBytes(ClientPackets.CantSpectate, [1, 2, 3, 4]));
		// no exception -> success
	}

	[Fact]
	public async Task Dispatch_MultiplePacketsInOneRequest_InvokesEachHandler()
	{
		var calls = new List<ClientPackets>();
		var pingHandler = new FakeHandler(ClientPackets.Ping, true, (_, _) => calls.Add(ClientPackets.Ping));
		var logoutHandler = new FakeHandler(ClientPackets.Logout, true, (_, r) =>
		{
			r.ReadI32();
			calls.Add(ClientPackets.Logout);
		});
		var dispatcher =
			new PacketDispatcher([pingHandler, logoutHandler], NullLogger<PacketDispatcher>.Instance);
		var session = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var body = PacketBytes(ClientPackets.Ping, [])
			.Concat(PacketBytes(ClientPackets.Logout, BinaryWriter.WriteInt32(0))).ToArray();

		await dispatcher.DispatchAsync(session, body);

		Assert.Equal([ClientPackets.Ping, ClientPackets.Logout], calls);
	}

	[Fact]
	public async Task Dispatch_RestrictedPlayer_OnlyInvokesRestrictedAllowedHandlers()
	{
		var pingCalled = false;
		var chatCalled = false;
		var pingHandler = new FakeHandler(ClientPackets.Ping, true, (_, _) => pingCalled = true);
		var chatHandler = new FakeHandler(ClientPackets.SendPublicMessage, false, (_, _) => chatCalled = true);
		var dispatcher =
			new PacketDispatcher([pingHandler, chatHandler], NullLogger<PacketDispatcher>.Instance);
		var restrictedSession =
			new GameSession(1, "cmyui", "token", UserPrivileges.Verified, DateTimeOffset.UnixEpoch); // restricted

		var body = PacketBytes(ClientPackets.Ping, []).Concat(PacketBytes(ClientPackets.SendPublicMessage, [1, 2]))
			.ToArray();
		await dispatcher.DispatchAsync(restrictedSession, body);

		Assert.True(pingCalled);
		Assert.False(chatCalled);
	}

	[Fact]
	public async Task Dispatch_ScoreUpdateFollowedByMatchComplete_InvokesBothHandlers()
	{
		// Regression test for the "Waiting for other players to finish" hang: the osu! client
		// batches the final MatchScoreUpdate and MatchComplete into one request body when a play
		// ends. MatchScoreUpdateHandler relays its payload via reader.ReadRaw(reader.RemainingLength),
		// so without per-packet reader scoping in the dispatcher it swallowed the trailing
		// MatchComplete, the server never broadcast MatchComplete, and every client waited forever.
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

		var dispatcher = new PacketDispatcher(
			[
				new MatchScoreUpdateHandler(fixture.MatchMembership, fixture.EventBus),
				new MatchCompleteHandler(fixture.MatchMembership, fixture.MatchRepository,
					NullLogger<MatchCompleteHandler>.Instance)
			],
			NullLogger<PacketDispatcher>.Instance);

		// 29-byte scoreframe (scoreV2 off) followed by an empty MatchComplete packet in one body.
		var body = PacketBytes(ClientPackets.MatchScoreUpdate, new byte[29])
			.Concat(PacketBytes(ClientPackets.MatchComplete, [])).ToArray();

		await dispatcher.DispatchAsync(guest, body);

		// The trailing MatchComplete must reach its handler and mark the slot complete rather than
		// being swallowed by the score-update handler's whole-buffer read.
		Assert.Equal(SlotStatus.Complete, match.Slots[1].Status);
		// Host is still playing, so the round is not closed yet.
		Assert.True(match.InProgress);
	}

	private sealed class FakeHandler(
		ClientPackets packetId,
		bool allowedWhenRestricted,
		Action<GameSession, PacketReader> onHandle) : IPacketHandler
	{
		public ClientPackets PacketId => packetId;
		public bool AllowedWhenRestricted => allowedWhenRestricted;

		public Task HandleAsync(GameSession userSession, PacketReader reader,
			CancellationToken cancellationToken = default)
		{
			onHandle(userSession, reader);
			return Task.CompletedTask;
		}
	}
}