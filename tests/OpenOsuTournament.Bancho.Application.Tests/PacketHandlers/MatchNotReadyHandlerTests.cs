using OpenOsuTournament.Bancho.Application.PacketHandlers.Multiplayer;
using OpenOsuTournament.Bancho.Domain.Multiplayer;
using OpenOsuTournament.Bancho.Protocol.Packets;
using static OpenOsuTournament.Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace OpenOsuTournament.Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchNotReady.</summary>
public class MatchNotReadyHandlerTests
{
    [Fact]
    public async Task Handle_SetsSlotStatusToNotReady()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.Slots[0].Status = SlotStatus.Ready;
        var handler = new MatchNotReadyHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.Equal(SlotStatus.NotReady, match.GetSlot(host.Id)!.Status);
    }
}