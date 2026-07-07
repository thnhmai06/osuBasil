using Bancho.Application.Commands;
using Bancho.Domain;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_lock.</summary>
public class MpLockCommandTests
{
    [Fact]
    public async Task HandleAsync_LocksOpenSlotsOnly()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.Slots[2].Status = SlotStatus.Open;
        var command = new MpLockCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("All unused slots locked.", response);
        Assert.Equal(SlotStatus.Locked, match.Slots[2].Status);
        Assert.Equal(SlotStatus.NotReady, match.Slots[0].Status);
    }
}
