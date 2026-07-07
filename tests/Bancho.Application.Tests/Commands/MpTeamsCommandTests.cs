using Bancho.Application.Commands;
using Bancho.Domain;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_teams.</summary>
public class MpTeamsCommandTests
{
    [Fact]
    public async Task HandleAsync_InvalidSyntax_ReturnsUsage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpTeamsCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("Invalid syntax: !mp teams <type>", response);
    }

    [Fact]
    public async Task HandleAsync_UnknownType_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpTeamsCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["nonsense"], match));

        Assert.Equal("Unknown team type. (ffa, tag, teams, tag-teams)", response);
    }

    [Fact]
    public async Task HandleAsync_TeamsVs_SetsOccupiedSlotsToRedAndResetsScrimIfScrimming()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.IsScrimming = true;
        match.AddMatchPoint(Application.Sessions.ScrimParticipant.OfPlayer(host.Id));
        var command = new MpTeamsCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["teams"], match));

        Assert.Equal("Match team type updated.", response);
        Assert.Equal(MatchTeamTypes.TeamVs, match.TeamType);
        Assert.Equal(MatchTeams.Red, match.Slots[0].Team);
        Assert.Empty(match.MatchPoints);
    }

    [Fact]
    public async Task HandleAsync_Ffa_SetsOccupiedSlotsToNeutral()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host, MatchTeamTypes.TeamVs);
        match.Slots[0].Team = MatchTeams.Red;
        var command = new MpTeamsCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["ffa"], match));

        Assert.Equal("Match team type updated.", response);
        Assert.Equal(MatchTeamTypes.HeadToHead, match.TeamType);
        Assert.Equal(MatchTeams.Neutral, match.Slots[0].Team);
    }
}
