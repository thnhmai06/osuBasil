using Bancho.Application.Commands;
using Bancho.Protocol;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_host.</summary>
public class MpHostCommandTests
{
    [Fact]
    public async Task HandleAsync_InvalidSyntax_ReturnsUsage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpHostCommand(fixture.SessionRegistry, fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("Invalid syntax: !mp host <name>", response);
    }

    [Fact]
    public async Task HandleAsync_TargetNotFound_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpHostCommand(fixture.SessionRegistry, fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["nobody"], match));

        Assert.Equal("Could not find a user by that name.", response);
    }

    [Fact]
    public async Task HandleAsync_TargetAlreadyHost_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpHostCommand(fixture.SessionRegistry, fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["host"], match));

        Assert.Equal("They're already host, silly!", response);
    }

    [Fact]
    public async Task HandleAsync_TargetNotInMatch_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var outsider = MakePlayer(2, "outsider");
        fixture.RegisterAll(host, outsider);
        var match = fixture.CreateMatch(host);
        var command = new MpHostCommand(fixture.SessionRegistry, fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["outsider"], match));

        Assert.Equal("Found no such player in the match.", response);
    }

    [Fact]
    public async Task HandleAsync_TargetInMatch_TransfersHostAndNotifiesTarget()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        guest.Dequeue();
        var command = new MpHostCommand(fixture.SessionRegistry, fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["guest"], match));

        Assert.Equal("Match host updated.", response);
        Assert.Equal(guest.Id, match.HostId);
        Assert.Contains(ServerPacketWriter.MatchTransferHost(), Chunk(guest.Dequeue()));
    }
}
