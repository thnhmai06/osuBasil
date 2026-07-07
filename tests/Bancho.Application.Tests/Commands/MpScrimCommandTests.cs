using Bancho.Application.Commands;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_scrim — the first chat command to actually flip MatchSession.IsScrimming.</summary>
public class MpScrimCommandTests
{
    [Fact]
    public async Task HandleAsync_InvalidSyntax_ReturnsUsage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpScrimCommand();

        var response = await command.HandleAsync(new MpCommandContext(host, ["not-a-number"], match));

        Assert.Equal("Invalid syntax: !mp scrim <bo#>", response);
    }

    [Fact]
    public async Task HandleAsync_EvenBestOf_ReturnsMustBeOdd()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpScrimCommand();

        var response = await command.HandleAsync(new MpCommandContext(host, ["bo4"], match));

        Assert.Equal("Best of must be an odd number!", response);
        Assert.False(match.IsScrimming);
    }

    [Fact]
    public async Task HandleAsync_ValidBestOf_StartsScrimWithWinningPoints()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpScrimCommand();

        var response = await command.HandleAsync(new MpCommandContext(host, ["bo5"], match));

        Assert.Equal("A scrimmage has been started by host; first to 3 points wins. Best of luck!", response);
        Assert.True(match.IsScrimming);
        Assert.Equal(3, match.WinningPoints);
    }

    [Fact]
    public async Task HandleAsync_AlreadyScrimming_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.IsScrimming = true;
        var command = new MpScrimCommand();

        var response = await command.HandleAsync(new MpCommandContext(host, ["bo5"], match));

        Assert.Equal("Already scrimming!", response);
    }

    /// <summary>
    /// The Python source has a "setting to 0 cancels the scrim" branch, but it's dead code:
    /// winning_pts = (best_of // 2) + 1 can never be 0 for any best_of in the validated 0-15
    /// range (it's always >= 1). "!mp scrim 0" therefore behaves like any other even best_of —
    /// rejected for not being odd — never reaching the cancel branch. Ported faithfully rather
    /// than "fixed", since !mp endscrim is the actual supported way to cancel a scrim.
    /// </summary>
    [Fact]
    public async Task HandleAsync_BestOfZero_NotScrimming_RejectedForBeingEven()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpScrimCommand();

        var response = await command.HandleAsync(new MpCommandContext(host, ["0"], match));

        Assert.Equal("Best of must be an odd number!", response);
    }

    [Fact]
    public async Task HandleAsync_BestOfZero_AlreadyScrimming_ReturnsAlreadyScrimming()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.IsScrimming = true;
        var command = new MpScrimCommand();

        var response = await command.HandleAsync(new MpCommandContext(host, ["0"], match));

        Assert.Equal("Already scrimming!", response);
    }
}
