using Basil.Server.Features.Multiplayer.Packets;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;
using static Basil.Server.Tests.Features.Multiplayer.Packets.MultiplayerTestSupport;

namespace Basil.Server.Tests.Features.Multiplayer.Packets;

/// <summary>Verifies the `MatchChangeTeam` handler toggles the player between red and blue.</summary>
public class MatchChangeTeamHandlerTests
{
	[Fact]
	public async Task Handle_TogglesBetweenRedAndBlue()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);
		var handler = new MatchChangeTeamHandler(fixture.MatchMembership);

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));
		Assert.Equal(MatchTeam.Blue, match.GetSlot(host.Id)!.Team);

		await handler.HandleAsync(host, new PacketReader(ReadOnlyMemory<byte>.Empty));
		Assert.Equal(MatchTeam.Red, match.GetSlot(host.Id)!.Team);
	}
}