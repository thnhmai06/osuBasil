using Bancho.Application.Commands;
using Bancho.Application.Sessions;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_endscrim.</summary>
public class MpEndScrimCommandTests
{
    [Fact]
    public async Task HandleAsync_NotScrimming_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpEndScrimCommand();

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("Not currently scrimming!", response);
    }

    [Fact]
    public async Task HandleAsync_Scrimming_EndsAndResetsScrimState()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.IsScrimming = true;
        match.AddMatchPoint(ScrimParticipant.OfPlayer(host.Id));
        var command = new MpEndScrimCommand();

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("Scrimmage ended.", response);
        Assert.False(match.IsScrimming);
        Assert.Empty(match.MatchPoints);
    }
}
