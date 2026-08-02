using Basil.Application.Packets.Multiplayer;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using static Basil.Application.Tests.Packets.MultiplayerTestSupport;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

/// <summary>Ported from app/api/domains/cho.py's MatchTransferHost.</summary>
public class MatchTransferHostHandlerTests
{
	private static PacketReader ReaderFor(int slotId)
	{
		return new PacketReader(BinaryWriter.WriteInt32(slotId));
	}

	[Fact]
	public async Task Handle_NonHost_NoOp()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		fixture.RegisterAll(host, guest);
		var match = fixture.CreateMatch(host);
		await fixture.MatchMembership.JoinAsync(guest, match, "");
		var handler = new MatchTransferHostHandler(fixture.SessionRegistry, fixture.MatchMembership,
			fixture.MatchRepository, NullLogger<MatchTransferHostHandler>.Instance);

		await handler.HandleAsync(guest, ReaderFor(1));

		Assert.Equal(host.Id, match.HostId);
	}

	[Fact]
	public async Task Handle_HostTransfersToOccupiedSlot_UpdatesHostAndNotifies()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		fixture.RegisterAll(host, guest);
		var match = fixture.CreateMatch(host);
		await fixture.MatchMembership.JoinAsync(guest, match, "");
		guest.Dequeue();
		var handler = new MatchTransferHostHandler(fixture.SessionRegistry, fixture.MatchMembership,
			fixture.MatchRepository, NullLogger<MatchTransferHostHandler>.Instance);

		await handler.HandleAsync(host, ReaderFor(1));

		Assert.Equal(guest.Id, match.HostId);
		Assert.Contains(ServerPacketWriter.MatchTransferHost(), Chunk(guest.Dequeue()));
	}

	[Fact]
	public async Task Handle_TargetSlotEmpty_NoOp()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);
		var handler = new MatchTransferHostHandler(fixture.SessionRegistry, fixture.MatchMembership,
			fixture.MatchRepository, NullLogger<MatchTransferHostHandler>.Instance);

		await handler.HandleAsync(host, ReaderFor(4));

		Assert.Equal(host.Id, match.HostId);
	}
}