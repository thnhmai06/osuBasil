using Bancho.Application.Commands;
using Bancho.Domain;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_unlock.</summary>
public class MpUnlockCommandTests
{
    [Fact]
    public async Task HandleAsync_UnlocksLockedSlotsOnly()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.Slots[2].Status = SlotStatus.Locked;
        var command = new MpUnlockCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("All locked slots unlocked.", response);
        Assert.Equal(SlotStatus.Open, match.Slots[2].Status);
        Assert.Equal(SlotStatus.NotReady, match.Slots[0].Status);
    }
}
