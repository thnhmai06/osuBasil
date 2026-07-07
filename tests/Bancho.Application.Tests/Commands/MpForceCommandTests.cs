using Bancho.Application.Commands;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_force — overrides password/silences, Administrator-only.</summary>
public class MpForceCommandTests
{
    [Fact]
    public async Task HandleAsync_InvalidSyntax_ReturnsUsage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpForceCommand(fixture.SessionRegistry, fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("Invalid syntax: !mp force <name>", response);
    }

    [Fact]
    public async Task HandleAsync_TargetNotFound_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpForceCommand(fixture.SessionRegistry, fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["nobody"], match));

        Assert.Equal("Could not find a user by that name.", response);
    }

    [Fact]
    public async Task HandleAsync_TargetFound_JoinsThemBypassingPassword()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var outsider = MakePlayer(2, "outsider");
        fixture.RegisterAll(host, outsider);
        var match = fixture.CreateMatch(host);
        match.Password = "secret";
        var command = new MpForceCommand(fixture.SessionRegistry, fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["outsider"], match));

        Assert.Equal("Welcome.", response);
        Assert.Same(match, outsider.Match);
    }
}
