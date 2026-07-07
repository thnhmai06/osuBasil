using Bancho.Application.Commands;
using Bancho.Domain;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_condition, minus the "pp" special case (no-pp scope).</summary>
public class MpConditionCommandTests
{
    [Fact]
    public async Task HandleAsync_InvalidSyntax_ReturnsUsage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpConditionCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("Invalid syntax: !mp condition <type>", response);
    }

    [Fact]
    public async Task HandleAsync_Pp_ReturnsNotSupportedMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpConditionCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["pp"], match));

        Assert.Equal("PP is not supported as a win condition on this server.", response);
        Assert.Equal(MatchWinConditions.Score, match.WinCondition);
    }

    [Fact]
    public async Task HandleAsync_UnknownCondition_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpConditionCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["nonsense"], match));

        Assert.Equal("Invalid win condition. (score, acc, combo, scorev2)", response);
    }

    [Fact]
    public async Task HandleAsync_Acc_SetsAccuracyWinCondition()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpConditionCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["acc"], match));

        Assert.Equal("Match win condition updated.", response);
        Assert.Equal(MatchWinConditions.Accuracy, match.WinCondition);
    }
}
