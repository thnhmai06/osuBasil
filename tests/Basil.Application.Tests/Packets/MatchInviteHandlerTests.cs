using Basil.Application.Packets.Multiplayer;
using Basil.Protocol.Packets;
using static Basil.Application.Tests.Packets.MultiplayerTestSupport;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

/// <summary>Verifies the `MatchInvite` handler invites the target player into the sender's match.</summary>
public class MatchInviteHandlerTests
{
	private static PacketReader ReaderFor(int userId)
	{
		return new PacketReader(BinaryWriter.WriteInt32(userId));
	}

	[Fact]
	public async Task Handle_NotInAMatch_NoOp()
	{
		var fixture = new Fixture();
		var player = MakePlayer(1, "alice");
		var target = MakePlayer(2, "bob");
		fixture.RegisterAll(player, target);
		var handler = new MatchInviteHandler(fixture.SessionRegistry);

		await handler.HandleAsync(player, ReaderFor(2));

		Assert.Empty(target.Dequeue());
	}

	[Fact]
	public async Task Handle_ValidTarget_SendsMatchInvite()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var target = MakePlayer(2, "bob");
		fixture.RegisterAll(host, target);
		var match = fixture.CreateMatch(host);
		var handler = new MatchInviteHandler(fixture.SessionRegistry);

		await handler.HandleAsync(host, ReaderFor(2));

		Assert.Contains(ServerPacketWriter.MatchInvite(host.Id, host.Name, match.Embed, target.Name),
			Chunk(target.Dequeue()));
	}
}