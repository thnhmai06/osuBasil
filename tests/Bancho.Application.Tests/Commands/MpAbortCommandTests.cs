using Bancho.Application.Commands;
using Bancho.Domain;
using Bancho.Protocol;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_abort.</summary>
public class MpAbortCommandTests
{
    [Fact]
    public async Task HandleAsync_NotInProgress_ReturnsAbortWhat()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpAbortCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("Abort what?", response);
    }

    [Fact]
    public async Task HandleAsync_InProgress_AbortsAndBroadcasts()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.InProgress = true;
        match.Slots[0].Status = SlotStatus.Playing;
        match.Slots[0].Loaded = true;
        host.Dequeue();
        var command = new MpAbortCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("Match aborted.", response);
        Assert.False(match.InProgress);
        Assert.Equal(SlotStatus.NotReady, match.Slots[0].Status);
        Assert.False(match.Slots[0].Loaded);
        Assert.Contains(ServerPacketWriter.MatchAbort(), Chunk(host.Dequeue()));
    }
}
