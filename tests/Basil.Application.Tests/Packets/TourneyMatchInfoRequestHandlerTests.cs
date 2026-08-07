using Basil.Application.Packets.Multiplayer;
using Basil.Application.Services.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using static Basil.Application.Tests.Packets.MultiplayerTestSupport;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

/// <summary>Verifies the `TourneyMatchInfoRequest` handler sends the current match state without its password, donator-only.</summary>
public class TourneyMatchInfoRequestHandlerTests
{
	private static PacketReader ReaderFor(int matchId)
	{
		return new PacketReader(BinaryWriter.WriteInt32(matchId));
	}

	[Fact]
	public async Task Handle_NonDonator_NoOp()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var requester = MakePlayer(2, "req");
		fixture.RegisterAll(host, requester);
		var match = fixture.CreateMatch(host);
		var handler = new TourneyMatchInfoRequestHandler(fixture.MatchRegistry,
			NullLogger<TourneyMatchInfoRequestHandler>.Instance);

		await handler.HandleAsync(requester, ReaderFor(match.Id));

		Assert.Empty(requester.Dequeue());
	}

	[Fact]
	public async Task Handle_Donator_SendsUpdateMatchWithoutPassword()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var requester = MakePlayer(2, "req");
		requester.Privilege = UserPrivileges.Unrestricted | UserPrivileges.Supporter;
		fixture.RegisterAll(host, requester);
		var match = fixture.CreateMatch(host);
		var handler = new TourneyMatchInfoRequestHandler(fixture.MatchRegistry,
			NullLogger<TourneyMatchInfoRequestHandler>.Instance);

		await handler.HandleAsync(requester, ReaderFor(match.Id));

		Assert.Contains(ServerPacketWriter.UpdateMatch(match.ToPacket(), false),
			Chunk(requester.Dequeue()));
	}
}