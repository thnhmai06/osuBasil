using Bancho.Application.Commands;
using Bancho.Protocol;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_invite.</summary>
public class MpInviteCommandTests
{
    [Fact]
    public async Task HandleAsync_InvalidSyntax_ReturnsUsage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpInviteCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("Invalid syntax: !mp invite <name>", response);
    }

    [Fact]
    public async Task HandleAsync_TargetNotFound_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpInviteCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(host, ["nobody"], match));

        Assert.Equal("Could not find a user by that name.", response);
    }

    [Fact]
    public async Task HandleAsync_TargetIsSelf_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(5, "host"); // id 5, not 1 — CommandTargetResolver.BotId is 1
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpInviteCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(host, ["host"], match));

        Assert.Equal("You can't invite yourself!", response);
    }

    [Fact]
    public async Task HandleAsync_TargetFound_SendsInvitePacket()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        guest.Dequeue();
        var command = new MpInviteCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(host, ["guest"], match));

        Assert.Equal("Invited guest to the match.", response);
        Assert.Contains(ServerPacketWriter.MatchInvite(host.Id, host.Name, match.Embed, guest.Name), Chunk(guest.Dequeue()));
    }
}
